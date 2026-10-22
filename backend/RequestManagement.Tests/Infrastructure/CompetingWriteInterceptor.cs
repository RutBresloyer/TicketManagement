using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RequestManagement.Api.Entities;

namespace RequestManagement.Tests.Infrastructure;

/// <summary>
/// Deterministically reproduces the race window: when the API calls SaveChanges (after it has read the row and passed
/// its own version pre-check), this interceptor first commits a competing UPDATE on a separate connection.
/// Only the database-enforced <c>WHERE RowVersion = @expected</c> can still detect the conflict at that point.
/// </summary>
public sealed class CompetingWriteInterceptor(string connectionString) : SaveChangesInterceptor
{
    private long _armedRequestId;
    private RequestStatus _competingStatus;

    public void Arm(long requestId, RequestStatus competingStatus)
    {
        _competingStatus = competingStatus;
        Interlocked.Exchange(ref _armedRequestId, requestId);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var requestId = Interlocked.Exchange(ref _armedRequestId, 0);
        if (requestId != 0)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE dbo.Requests SET Status = @status, UpdatedAt = TODATETIMEOFFSET(SYSUTCDATETIME(), 0) WHERE Id = @id";
            command.Parameters.AddWithValue("@status", (byte)_competingStatus);
            command.Parameters.AddWithValue("@id", requestId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return result;
    }
}
