var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres-server", password: postgresPassword)
    .WithDataVolume("fiore-aspire-postgres-data")
    .AddDatabase("Postgres", "fiore");

var redis = builder.AddRedis("Redis");

builder.AddProject<Projects.Fiore_Api>("fiore-api")
    .WithReference(postgres)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WithExternalHttpEndpoints();

builder.Build().Run();
