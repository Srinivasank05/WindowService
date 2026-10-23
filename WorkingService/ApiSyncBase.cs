using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace WorkingService.Service;

public abstract class ApiSyncBase
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;
    private readonly int _batchSize;
    private readonly HashSet<long> _sentIds = new();   // separate memory for each class

    // ---- each child class must fill these ----
    public abstract string SyncName { get; }            // used in logs, e.g. "Modality"
    protected abstract string ListName { get; }         // JSON array name, e.g. "StudyDetails"
    protected abstract string ConfigSection { get; }    // e.g. "ModalitySourceApi"
    protected abstract string ClientUrlKey { get; }     // e.g. "ClientApi:Url"

    // child class can override if the id column has a different name
    protected virtual string IdField => "Id";

    // ---- values read from appsettings.json ----
    private string GetUrl => _config[$"{ConfigSection}:GetUrl"]!;
    private string UptUrl => _config[$"{ConfigSection}:UptUrl"]!;
    private string OrgId => _config[$"{ConfigSection}:OrgId"]!;
    private string ClientUrl => _config[ClientUrlKey]!;

    protected ApiSyncBase(IConfiguration config, HttpClient http)
    {
        _config = config;
        _http = http;
        _batchSize = config.GetValue("Service:BatchSize", 5);
    }

    // ------------------------------------------------------------
    // Main flow: get -> filter -> send in batches -> update once
    // ------------------------------------------------------------
    //public async Task RunOnceAsync()
    //{
    //    // Step 1: get the JSON from the source API
    //    string json = await _http.GetStringAsync($"{GetUrl}?Orgid={OrgId}");

    //    // Step 2: all ids in the response
    //    List<long> allIds = GetIdsFromJson(json);

    //    // Step 3: keep only ids that were not sent before
    //    List<long> idsToSend = allIds.Where(id => !_sentIds.Contains(id)).ToList();

    //    LogWriter.LogInfo($"[{SyncName}] Source API returned {allIds.Count} records, {idsToSend.Count} to send");

    //    var results = new Dictionary<long, (bool IsSuccess, string ResultText)>();

    //    // Step 4: send in batches
    //    for (int start = 0; start < idsToSend.Count; start += _batchSize)
    //    {
    //        List<long> batchIds = idsToSend.Skip(start).Take(_batchSize).ToList();
    //        await ProcessBatchAsync(json, batchIds, results);
    //    }

    //    // Step 5: update the source table one time for all batches
    //    if (results.Count > 0)
    //    {
    //        await UpdateTableAsync(results);
    //    }
    //}

    private async Task ProcessBatchAsync(string json, List<long> batchIds,
        Dictionary<long, (bool IsSuccess, string ResultText)> results)
    {
        string payload = BuildPayload(json, batchIds);
        var (isSuccess, resultText) = await SendToClientAsync(payload);

        foreach (long id in batchIds)
        {
            results[id] = (isSuccess, resultText);
            if (isSuccess) _sentIds.Add(id);
        }
    }

    private List<long> GetIdsFromJson(string json)
    {
        var ids = new List<long>();
        JsonArray records = JsonNode.Parse(json)![ListName]!.AsArray();

        foreach (JsonNode? record in records)
        {
            if (record == null || record[IdField] == null)
                throw new Exception($"[{SyncName}] Column '{IdField}' not found in {ListName}");

            ids.Add((long)record[IdField]!);
        }

        return ids;
    }

    // child class can override this if the client needs a different payload
    protected virtual string BuildPayload(string json, List<long> batchIds)
    {
        JsonNode root = JsonNode.Parse(json)!;
        JsonArray records = root[ListName]!.AsArray();

        // go from last to first so removing a record is safe
        for (int i = records.Count - 1; i >= 0; i--)
        {
            JsonNode record = records[i]!;
            long id = (long)record[IdField]!;

            if (batchIds.Contains(id))
                record.AsObject().Remove(IdField);   // client should not get the Id
            else
                records.RemoveAt(i);                 // not in this batch
        }

        return root.ToJsonString();
    }

    // child class can override this if the client has a different success rule
    protected virtual bool IsClientSuccess(HttpStatusCode status, string resultText)
    {
        return status == HttpStatusCode.OK &&
               (resultText.Contains("done") || resultText.Contains("StatusCode"));
    }

    private async Task<(bool, string)> SendToClientAsync(string payload)
    {
        try
        {
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(ClientUrl, content);
            string resultText = await response.Content.ReadAsStringAsync();

            return (IsClientSuccess(response.StatusCode, resultText), resultText);
        }
        catch (Exception ex)
        {
            // could not reach the client (network down, timeout ...)
            return (false, ex.Message);
        }
    }

    private async Task UpdateTableAsync1(Dictionary<long, (bool IsSuccess, string ResultText)> results)
    {
        var payload = results.Select(r => new
        {
            Id = r.Key,
            ProcessStatus = r.Value.IsSuccess ? "Y" : "E",
            ResponseMessage = r.Value.IsSuccess ? "Success: data sent to client" : "Failure: " + r.Value.ResultText,
            ClientResponse = r.Value.ResultText,
            ModifiedAT = DateTime.Now
        }).ToList();

        try
        {
            using var response = await _http.PostAsJsonAsync(UptUrl, payload);
            string body = await response.Content.ReadAsStringAsync();

            LogWriter.LogInfo($"[{SyncName}] Update status: {(int)response.StatusCode}. Ids {string.Join(",", results.Keys)}. API: {body}");
        }
        catch (Exception ex)
        {
            LogWriter.LogInfo($"[{SyncName}] Update exception: {ex.Message}. Ids {string.Join(",", results.Keys)}");
        }
    }

    private async Task UpdateTableAsync(Dictionary<long, (bool IsSuccess, string ResultText)> results)
    {
        var payload = results.Select(r => new
        {
            Id = r.Key,
            ProcessStatus = r.Value.IsSuccess ? "Y" : "E",
            ResponseMessage = r.Value.IsSuccess ? "Success: data sent to client" : "Failure: " + r.Value.ResultText,
            ClientResponse = r.Value.ResultText,
            ModifiedAT = DateTime.Now
        }).ToList();

        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync(UptUrl, content);
            string body = await response.Content.ReadAsStringAsync();

            LogWriter.LogInfo($"[{SyncName}] Update status: {(int)response.StatusCode}. Ids {string.Join(",", results.Keys)}. API: {body}");
        }
        catch (Exception ex)
        {
            LogWriter.LogInfo($"[{SyncName}] Update exception: {ex.Message}. Ids {string.Join(",", results.Keys)}");
        }
    }
}