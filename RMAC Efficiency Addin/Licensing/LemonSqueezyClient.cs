using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RMAC_Efficiency_Addin.Licensing
{
    internal sealed class ActivateResult
    {
        public bool Success { get; init; }
        public string? InstanceId { get; init; }
        public string? LicenseStatus { get; init; }
        public string? Error { get; init; }
    }

    internal sealed class ValidateResult
    {
        public bool Valid { get; init; }
        public string? LicenseStatus { get; init; }
        public string? Error { get; init; }
    }

    internal static class LemonSqueezyClient
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private const string BaseUrl = "https://api.lemonsqueezy.com";

        private const int ExpectedStoreId = 311732;
        private const int ExpectedProductId = 882869;

        public static async Task<ActivateResult> ActivateAsync(string licenseKey, string instanceName)
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("license_key", licenseKey),
                    new KeyValuePair<string, string>("instance_name", instanceName)
                });

                var response = await _http.PostAsync($"{BaseUrl}/v1/licenses/activate", content)
                    .ConfigureAwait(false);

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<ApiActivateResponse>(json);

                if (result == null)
                    return new ActivateResult { Error = "Empty response from server." };

                if (!result.Activated || result.Instance == null)
                    return new ActivateResult { Error = result.Error ?? "Activation failed." };

                // Verify the key belongs to our product
                if (ExpectedStoreId > 0 && result.Meta?.StoreId != ExpectedStoreId)
                    return new ActivateResult { Error = "License key is not valid for this product." };
                if (ExpectedProductId > 0 && result.Meta?.ProductId != ExpectedProductId)
                    return new ActivateResult { Error = "License key is not valid for this product." };

                return new ActivateResult
                {
                    Success = true,
                    InstanceId = result.Instance.Id,
                    LicenseStatus = result.LicenseKey?.Status
                };
            }
            catch (HttpRequestException ex)
            {
                return new ActivateResult { Error = $"Network error: {ex.Message}" };
            }
            catch (TaskCanceledException)
            {
                return new ActivateResult { Error = "Request timed out." };
            }
            catch (Exception ex)
            {
                return new ActivateResult { Error = ex.Message };
            }
        }

        public static async Task<ValidateResult> ValidateAsync(string licenseKey, string instanceId)
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("license_key", licenseKey),
                    new KeyValuePair<string, string>("instance_id", instanceId)
                });

                var response = await _http.PostAsync($"{BaseUrl}/v1/licenses/validate", content)
                    .ConfigureAwait(false);

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<ApiValidateResponse>(json);

                if (result == null)
                    return new ValidateResult { Error = "Empty response from server." };

                return new ValidateResult
                {
                    Valid = result.Valid,
                    LicenseStatus = result.LicenseKey?.Status,
                    Error = result.Error
                };
            }
            catch (HttpRequestException ex)
            {
                return new ValidateResult { Error = $"Network error: {ex.Message}" };
            }
            catch (TaskCanceledException)
            {
                return new ValidateResult { Error = "Request timed out." };
            }
            catch (Exception ex)
            {
                return new ValidateResult { Error = ex.Message };
            }
        }

        public static async Task<bool> DeactivateAsync(string licenseKey, string instanceId)
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("license_key", licenseKey),
                    new KeyValuePair<string, string>("instance_id", instanceId)
                });

                var response = await _http.PostAsync($"{BaseUrl}/v1/licenses/deactivate", content)
                    .ConfigureAwait(false);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // --- API response DTOs ---

        private sealed class ApiActivateResponse
        {
            [JsonPropertyName("activated")]
            public bool Activated { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("license_key")]
            public ApiLicenseKey? LicenseKey { get; set; }

            [JsonPropertyName("instance")]
            public ApiInstance? Instance { get; set; }

            [JsonPropertyName("meta")]
            public ApiMeta? Meta { get; set; }
        }

        private sealed class ApiValidateResponse
        {
            [JsonPropertyName("valid")]
            public bool Valid { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("license_key")]
            public ApiLicenseKey? LicenseKey { get; set; }

            [JsonPropertyName("instance")]
            public ApiInstance? Instance { get; set; }

            [JsonPropertyName("meta")]
            public ApiMeta? Meta { get; set; }
        }

        private sealed class ApiLicenseKey
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("status")]
            public string? Status { get; set; }

            [JsonPropertyName("key")]
            public string? Key { get; set; }

            [JsonPropertyName("activation_limit")]
            public int ActivationLimit { get; set; }

            [JsonPropertyName("activation_usage")]
            public int ActivationUsage { get; set; }

            [JsonPropertyName("created_at")]
            public string? CreatedAt { get; set; }

            [JsonPropertyName("expires_at")]
            public string? ExpiresAt { get; set; }
        }

        private sealed class ApiInstance
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("created_at")]
            public string? CreatedAt { get; set; }
        }

        private sealed class ApiMeta
        {
            [JsonPropertyName("store_id")]
            public int StoreId { get; set; }

            [JsonPropertyName("order_id")]
            public int OrderId { get; set; }

            [JsonPropertyName("product_id")]
            public int ProductId { get; set; }

            [JsonPropertyName("product_name")]
            public string? ProductName { get; set; }

            [JsonPropertyName("variant_id")]
            public int VariantId { get; set; }

            [JsonPropertyName("variant_name")]
            public string? VariantName { get; set; }

            [JsonPropertyName("customer_id")]
            public int CustomerId { get; set; }

            [JsonPropertyName("customer_name")]
            public string? CustomerName { get; set; }

            [JsonPropertyName("customer_email")]
            public string? CustomerEmail { get; set; }
        }
    }
}
