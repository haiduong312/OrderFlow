using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Application.Persistence;

public sealed class OrderFlowDesignTimeFactory : IDesignTimeDbContextFactory<OrderFlowDbContext>
{
    public OrderFlowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderFlowDbContext>();
        PersistenceRegistration.Configure(options, Environment.GetEnvironmentVariable("ConnectionStrings__OrderFlow"));
        return new OrderFlowDbContext(options.Options);
    }
}
