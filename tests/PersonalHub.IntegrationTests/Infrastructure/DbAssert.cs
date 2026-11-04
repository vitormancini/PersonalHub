using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PersonalHub.IntegrationTests.Infrastructure;

public static class DbAssert
{
    /// <summary>Garante que o banco recusou a gravação pela constraint esperada.</summary>
    public static async Task ViolatesAsync(string sqlState, string constraintName, Func<Task> save)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(save);
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        Assert.Equal(constraintName, postgres.ConstraintName);
    }
}
