-- Infrastructure identities only, run by the official image only on a NEW data directory.
-- Passwords stay in mounted secret files, never command arguments/source/Compose environment.
SELECT format('CREATE ROLE orderflow_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', rtrim(pg_read_file('/run/secrets/migrator_password'), E'\n')) \gexec
SELECT format('CREATE ROLE orderflow_api LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', rtrim(pg_read_file('/run/secrets/api_password'), E'\n')) \gexec
SELECT format('CREATE ROLE orderflow_worker LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', rtrim(pg_read_file('/run/secrets/worker_password'), E'\n')) \gexec
SELECT format('CREATE ROLE orderflow_simulator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', rtrim(pg_read_file('/run/secrets/simulator_password'), E'\n')) \gexec
ALTER DATABASE orderflow OWNER TO orderflow_migrator;
REVOKE ALL ON DATABASE orderflow FROM PUBLIC;
GRANT CONNECT ON DATABASE orderflow TO orderflow_migrator, orderflow_api, orderflow_worker;
CREATE DATABASE orderflow_simulator OWNER orderflow_simulator;
REVOKE ALL ON DATABASE orderflow_simulator FROM PUBLIC;
GRANT CONNECT ON DATABASE orderflow_simulator TO orderflow_simulator;
