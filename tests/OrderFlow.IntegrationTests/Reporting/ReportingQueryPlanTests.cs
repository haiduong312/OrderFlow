using System.Text.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using OrderFlow.Application.Modules.Reporting;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace OrderFlow.IntegrationTests.Reporting;

[Collection("PostgreSQL"), Trait("Category", "PostgreSQL")]
public sealed class ReportingQueryPlanTests(PostgresServer server, ITestOutputHelper output) : PostgresTest(server)
{
    [Fact]
    public async Task Selective_time_and_order_access_paths_are_measured_before_choosing_indexes()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        // Synthetic planner fixture, not fake financial business data. Temporary tables avoid
        // bypassing any production FK/ledger guard just to manufacture 100,000 rows.
        await Execute("""
            CREATE TEMP TABLE report_plan AS
            SELECT md5(g::text)::uuid AS "Id", md5((g % 10000)::text)::uuid AS "OrderId",
                timestamptz '2034-01-01 00:00:00Z' + (g % 365) * interval '1 day' AS "OccurredAt",
                CASE WHEN g % 5 = 0 THEN 'Pending' ELSE 'Succeeded' END AS "Status",
                12.50::numeric(18,2) AS "Amount", repeat('x', 200) AS padding
            FROM generate_series(1,100000) g;
            ANALYZE report_plan;
            """);
        var directory = Path.Combine(Path.GetTempPath(), "orderflow-reporting", "plans");
        Directory.CreateDirectory(directory);
        var cases = new[]
        {
            ("date-range", """SELECT SUM("Amount") FROM report_plan WHERE "OccurredAt" >= timestamptz '2034-06-01 00:00:00+00' AND "OccurredAt" < timestamptz '2034-06-02 00:00:00+00'""",
                """CREATE INDEX report_plan_candidate ON report_plan ("OccurredAt", "Id")"""),
            ("order-audit", """SELECT "Id", "OccurredAt" FROM report_plan WHERE "OrderId" = md5('42')::uuid ORDER BY "OccurredAt" DESC, "Id" DESC LIMIT 20""",
                """CREATE INDEX report_plan_candidate ON report_plan ("OrderId", "OccurredAt", "Id")"""),
            ("succeeded-refunds", """SELECT SUM("Amount") FROM report_plan WHERE "Status" = 'Succeeded' AND "OccurredAt" >= timestamptz '2034-06-01 00:00:00+00' AND "OccurredAt" < timestamptz '2034-06-02 00:00:00+00'""",
                """CREATE INDEX report_plan_candidate ON report_plan ("OccurredAt", "Id") WHERE "Status" = 'Succeeded'""")
        };
        foreach (var (name, query, index) in cases)
        {
            var before = await Explain(query);
            await Execute(index); await Execute("ANALYZE report_plan");
            var after = await Explain(query);
            await File.WriteAllTextAsync(Path.Combine(directory, name + "-before.json"), before);
            await File.WriteAllTextAsync(Path.Combine(directory, name + "-after.json"), after);
            Assert.Contains("report_plan_candidate", after, StringComparison.Ordinal);
            output.WriteLine($"{name}: before={Summary(before)}; after={Summary(after)}; plans={directory}");
            await Execute("DROP INDEX report_plan_candidate");
        }

        async Task Execute(string sql)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(budget.Token);
        }
        async Task<string> Explain(string sql)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql, connection);
            return (string)(await command.ExecuteScalarAsync(budget.Token))!;
        }
        static string Summary(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement[0]; var plan = root.GetProperty("Plan");
            return $"{plan.GetProperty("Node Type").GetString()}, total cost={plan.GetProperty("Total Cost")}, actual rows={plan.GetProperty("Actual Rows")}, execution ms={root.GetProperty("Execution Time")}";
        }
    }

    [Fact]
    public async Task Actual_report_SQL_is_explained_against_the_financial_correctness_fixture()
    {
        await using var scenario = await ReportingScenario.CreateAsync(Database);
        var capture = new ReportCommands();
        var options = new DbContextOptionsBuilder<OrderFlowDbContext>(Database.Options()).AddInterceptors(capture).Options;
        await using (var context = new OrderFlowDbContext(options))
        {
            var service = new ReportingService(context, scenario.Clock);
            var range = new ReportRangeQuery { From = new(2035, 1, 2), To = new(2035, 1, 4) };
            await service.SalesAsync(range, default);
            await service.CountsAsync(range, default);
            await service.TopAsync(new TopProductsQuery { From = range.From, To = range.To }, default);
            await service.LowStockAsync(new LowStockQuery { Threshold = 95 }, default);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        var directory = Path.Combine(Path.GetTempPath(), "orderflow-reporting", "plans");
        Directory.CreateDirectory(directory);
        Assert.Equal(5, capture.Commands.Count); // Sales, counts, top, low-stock count + page.
        var index = 0;
        foreach (var (sql, parameters) in capture.Commands)
        {
            await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var plan = (string)(await command.ExecuteScalarAsync(budget.Token))!;
            await File.WriteAllTextAsync(Path.Combine(directory, $"actual-report-{++index}.json"), plan, budget.Token);
            output.WriteLine($"Actual report SQL {index}: plan saved under {directory}; tiny fixture, no latency/load claim.");
        }
    }

    private sealed class ReportCommands : DbCommandInterceptor
    {
        public List<(string Sql, (string Name, object Value)[] Parameters)> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value!)).ToArray()));
            return ValueTask.FromResult(result);
        }
    }
}
