namespace Veda.Api.Controllers;

[ApiController]
[Route("api/datasources")]
public class DataSourcesController(IEnumerable<IDataSourceConnector> connectors) : ControllerBase
{
    [HttpGet]
    public IActionResult List() =>
        Ok(connectors.Select(c => new
        {
            name        = c.Name,
            description = c.Description,
            enabled     = c.Enabled
        }));

    [HttpPost("sync")]
    public async Task<IActionResult> SyncAll(CancellationToken ct)
    {
        var results = new List<DataSourceSyncResult>();
        foreach (var connector in connectors.Where(c => c.Enabled))
        {
            var result = await connector.SyncAsync(ct);
            results.Add(result);
        }
        return Ok(results);
    }

    [HttpPost("{name}/sync")]
    public async Task<IActionResult> SyncOne(string name, CancellationToken ct)
    {
        var connector = connectors.FirstOrDefault(
            c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (connector is null)
            return NotFound(new { error = $"Connector '{name}' not found." });

        var result = await connector.SyncAsync(ct);
        return Ok(result);
    }
}
