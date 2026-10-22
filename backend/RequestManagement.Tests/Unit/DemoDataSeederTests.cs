using System.Data;
using RequestManagement.Api.Data;
using RequestManagement.Api.Entities;
using RequestManagement.Api.Services;

namespace RequestManagement.Tests.Unit;

public sealed class DemoDataSeederTests
{
    [Fact]
    public void Generation_is_deterministic()
    {
        var (requestsA, historyA) = DemoDataSeeder.Generate(2_000);
        var (requestsB, historyB) = DemoDataSeeder.Generate(2_000);

        Assert.Equal(Rows(requestsA), Rows(requestsB));
        Assert.Equal(Rows(historyA), Rows(historyB));
    }

    [Fact]
    public void Generated_history_follows_the_transition_rules_and_matches_the_final_row()
    {
        var (requests, history) = DemoDataSeeder.Generate(5_000);
        var historyByRequest = history.Rows.Cast<DataRow>().ToLookup(r => (long)r["RequestId"]);

        foreach (DataRow request in requests.Rows)
        {
            var id = (long)request["Id"];
            var createdAt = (DateTimeOffset)request["CreatedAt"];
            var previousStatus = RequestStatus.New;
            var previousTime = createdAt;

            foreach (var change in historyByRequest[id])
            {
                var from = (RequestStatus)(byte)change["PreviousStatus"];
                var to = (RequestStatus)(byte)change["NewStatus"];
                var at = (DateTimeOffset)change["ChangedAt"];
                Assert.Equal(previousStatus, from);
                Assert.True(RequestStatusTransitions.IsAllowed(from, to), $"{from} -> {to} for request {id}");
                Assert.True(at >= previousTime && at <= DemoDataSeeder.TimeAnchorUtc);
                previousStatus = to;
                previousTime = at;
            }

            Assert.Equal(previousStatus, (RequestStatus)(byte)request["Status"]);
            Assert.Equal(previousTime, (DateTimeOffset)request["UpdatedAt"]);
        }
    }

    [Fact]
    public void Full_dataset_has_the_documented_shape()
    {
        var (requests, _) = DemoDataSeeder.Generate(DemoDataSeeder.DefaultRequestCount);
        var rows = requests.Rows.Cast<DataRow>().ToList();

        Assert.Equal(100_000, rows.Count);
        Assert.Equal(200, rows.Select(r => (string)r["OrganizationName"]).Distinct().Count());
        Assert.Equal(4, rows.Select(r => (byte)r["Status"]).Distinct().Count());
        Assert.Equal(3, rows.Select(r => (byte)r["Priority"]).Distinct().Count());
        var unassignedShare = rows.Count(r => r["AssignedTo"] is DBNull) / (double)rows.Count;
        Assert.InRange(unassignedShare, 0.10, 0.20);
    }

    private static List<string> Rows(DataTable table) =>
        table.Rows.Cast<DataRow>().Select(r => string.Join("|", r.ItemArray)).ToList();
}
