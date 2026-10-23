using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WorkingService.Service
{
    public class PatientDemography : IDisposable
    {
        private readonly HttpClient _http;

        public string ServiceName = "PatientDemography";

        private const string IdField = "Id";   // id column name in the source JSON

        public PatientDemography(HttpClient http)
        {
            _http = http;
        }

        public async Task getPatientDemography()
        {
            // read config (same format as before)
            string GetPatientDemographyOrgIds = ServiceHelper.GetAppSettingAttributeValue("PatientDemographySourceApi", "arrOrgId");
            string GetPatientDemographyUrl = ServiceHelper.GetAppSettingAttributeValue("PatientDemographySourceApi", "GetUrl");
            string GetBatchSize = ServiceHelper.GetAppSettingAttributeValue("PatientDemographySourceApi", "BatchSize");

            string[] arrOrgIDs = GetPatientDemographyOrgIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            int batchSize = int.TryParse(GetBatchSize, out int size) && size > 0 ? size : 5;

            foreach (string Orgid in arrOrgIDs)
            {
                try
                {
                    string orgUrl = GetPatientDemographyUrl + "?Orgid=" + Orgid;

                    string json = await _http.GetStringAsync(orgUrl);

                    List<long> allIds = GetIdsFromJson(json);

                    LogWriter.LogInfo($"[{ServiceName}] Org {Orgid}: source returned {allIds.Count}, {allIds.Count} to send");

                    var results = new Dictionary<long, (bool IsSuccess, string ResultText)>();

                    for (int start = 0; start < allIds.Count; start += batchSize)
                    {
                        List<long> batchIds = allIds.Skip(start).Take(batchSize).ToList();
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
            }
        }

        // Source JSON root is an array: [ { "Id": 6, ... }, { "Id": 5, ... } ]
        private static List<long> GetIdsFromJson(string json)
        {
            var ids = new List<long>();
            JsonArray records = JsonNode.Parse(json)!.AsArray();

            foreach (JsonNode? record in records)
            {
                if (record == null || record[IdField] == null)
                    throw new Exception($"[PatientDemography] Column '{IdField}' not found in record");

                ids.Add((long)record[IdField]!);
            }

            return ids;
        }

        protected virtual string BuildPayload(string json, List<long> batchIds)
        {
            JsonArray records = JsonNode.Parse(json)!.AsArray();

           
            for (int i = records.Count - 1; i >= 0; i--)
            {
                long id = (long)records[i]![IdField]!;

                if (batchIds.Contains(id))
                    records[i]!.AsObject().Remove(IdField);   // client should not get the Id
                else
                    records.RemoveAt(i);                      // not in this batch
            }

            // sends: [ { PatientName..., Age... }, ... ]
            return records.ToJsonString();

           
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
                string postUrl = ServiceHelper.GetAppSettingAttributeValue("PatientDemographySourceApi", "ClientpostUrl");

                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(postUrl, content);
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
                string updateUrl = ServiceHelper.GetAppSettingAttributeValue("PatientDemographySourceApi", "UpdateUrl");

                string json = JsonSerializer.Serialize(rows);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _http.PostAsync(updateUrl, content);
                string body = await response.Content.ReadAsStringAsync();

                LogWriter.LogInfo($"[{ServiceName}] Update status: {(int)response.StatusCode}. Ids {string.Join(",", results.Keys)}. API: {body}");
            }
            catch (Exception ex)
            {
                LogWriter.LogInfo($"[{ServiceName}] Update exception: {ex.Message}. Ids {string.Join(",", results.Keys)}");
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}