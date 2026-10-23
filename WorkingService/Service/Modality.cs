using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WorkingService.Service
{
    public class Modality : IDisposable
    {
        private readonly HttpClient _http;
        private readonly HashSet<long> _sentIds = new();   // ids already sent (lost on restart)

        public string ServiceName = "Modality";
        public Modality(HttpClient http)
        {
            _http = http;
        }

        public async Task GetModalityDetails()
        {
           

            // read config (same format as before)
            string GetModalityOrgIds = ServiceHelper.GetAppSettingAttributeValue("ModalitySourceApi", "arrOrgId");
            string GetModalityUrl = ServiceHelper.GetAppSettingAttributeValue("ModalitySourceApi", "GetUrl");
            string GetBatchSize = ServiceHelper.GetAppSettingAttributeValue("ModalitySourceApi", "BatchSize");

            string[] arrOrgIDs = GetModalityOrgIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            int batchSize = int.TryParse(GetBatchSize, out int size) && size > 0 ? size : 5;

            foreach (string Orgid in arrOrgIDs)
            {
                try
                {
                 
                    string orgUrl = GetModalityUrl + "?Orgid=" + Orgid;

                    
                    string json = await _http.GetStringAsync(orgUrl);

                    
                    List<long> allIds = GetIdsFromJson(json);

                   
                    List<long> idsToSend = allIds.Where(id => !_sentIds.Contains(id)).ToList();

                    LogWriter.LogInfo($"[{ServiceName}] Org {Orgid}: source returned {allIds.Count}, {idsToSend.Count} to send");

                    var results = new Dictionary<long, (bool IsSuccess, string ResultText)>();

                   
                    for (int start = 0; start < idsToSend.Count; start += batchSize)
                    {
                        List<long> batchIds = idsToSend.Skip(start).Take(batchSize).ToList();
                        await ProcessBatchAsync(json, batchIds, results);
                    }

                    
                    if (results.Count > 0)
                        await UpdateTableAsync(results);
                }
                catch (Exception ex)
                {
                    
                    LogWriter.LogInfo($"[{ServiceName}] Org {Orgid} failed: {ex.Message}");
                }
            }
        }

        private async Task ProcessBatchAsync(
            string json, List<long> batchIds, Dictionary<long, (bool IsSuccess, string ResultText)> results)
        {
            string payload = BuildPayload(json, batchIds);
            (bool isSuccess, string resultText) = await SendToClientAsync(payload);

            foreach (long id in batchIds)
            {
                results[id] = (isSuccess, resultText);

                if (isSuccess)
                    _sentIds.Add(id);
            }
        }

        private static List<long> GetIdsFromJson(string json)
        {
           
            string IdField = "Id";        // id column name in the source JSON
            string ListName = "StudyDetails";     // CHANGE: array name in the source JSON

            var ids = new List<long>();
            JsonArray records = JsonNode.Parse(json)![ListName]!.AsArray();

            foreach (JsonNode? record in records)
            {
                if (record == null || record[IdField] == null)
                    throw new Exception($"[Modality] Column '{IdField}' not found in {ListName}");

                ids.Add((long)record[IdField]!);
            }

            return ids;
        }

        protected virtual string BuildPayload(string json, List<long> batchIds)
        {
            string IdField = "Id";        // id column name in the source JSON
            string ListName = "StudyDetails";     // CHANGE: array name in the source JSON

            JsonNode root = JsonNode.Parse(json)!;
            JsonArray records = root[ListName]!.AsArray();

            // go from last to first so removing a record is safe
            for (int i = records.Count - 1; i >= 0; i--)
            {
                long id = (long)records[i]![IdField]!;

                if (batchIds.Contains(id))
                    records[i]!.AsObject().Remove(IdField);   // client should not get the Id
                else
                    records.RemoveAt(i);                      // not in this batch
            }

            return root.ToJsonString();
        }

        protected virtual bool IsClientSuccess(HttpStatusCode status, string resultText)
        {
            return status == HttpStatusCode.OK &&
                   (resultText.Contains("done") || resultText.Contains("StatusCode"));
        }

        private async Task<(bool IsSuccess, string ResultText)> SendToClientAsync(string payload)
        {
            try
            {
                string postModalityUrl = ServiceHelper.GetAppSettingAttributeValue("ModalitySourceApi", "ClientpostUrl");

                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(postModalityUrl, content);
                string resultText = await response.Content.ReadAsStringAsync();

                return (IsClientSuccess(response.StatusCode, resultText), resultText);
            }
            catch (Exception ex)
            {
                // client not reachable (network down, timeout ...)
                return (false, ex.Message);
            }
        }

        private async Task UpdateTableAsync(Dictionary<long, (bool IsSuccess, string ResultText)> results)
        {
            var rows = results.Select(r => new
            {
                Id = r.Key,
                ProcessStatus = r.Value.IsSuccess ? "Y" : "E",
                ResponseMessage = r.Value.IsSuccess ? "Success: data sent to client" : "Failure: " + r.Value.ResultText,
                ClientResponse = r.Value.ResultText,
                ModifiedAT = DateTime.Now
            }).ToList();

            try
            {
                string updateModalityUrl = ServiceHelper.GetAppSettingAttributeValue("ModalitySourceApi", "UpdateUrl");

                string json = JsonSerializer.Serialize(rows);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _http.PostAsync(updateModalityUrl, content);
                string body = await response.Content.ReadAsStringAsync();

                LogWriter.LogInfo($"ModalitySourceApi Update status: {(int)response.StatusCode}. Ids {string.Join(",", results.Keys)}. API: {body}");
            }
            catch (Exception ex)
            {
                LogWriter.LogInfo($"ModalitySourceApi Update exception: {ex.Message}. Ids {string.Join(",", results.Keys)}");
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}