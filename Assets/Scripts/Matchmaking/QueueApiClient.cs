using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ShooterPrototype.Matchmaking
{
    [Serializable]
    public sealed class QueueEnqueueRequest
    {
        public string playerId;
    }

    [Serializable]
    public sealed class QueueEnqueueResponse
    {
        public string ticketId;
        public string status;
    }

    [Serializable]
    public sealed class QueueDequeueRequest
    {
        public string ticketId;
    }

    [Serializable]
    public sealed class QueueDequeueResponse
    {
        public bool success;
        public string status;
    }

    [Serializable]
    public sealed class QueueTicketStatusResponse
    {
        public string ticketId;
        public string playerId;
        public string status;
        public float queueDurationSeconds;
        public string matchId;
        public int matchedPlayerCount;
        public string serverAddress;
        public int serverPort;
    }

    [Serializable]
    public sealed class QueueLeaveMatchRequest
    {
        public string ticketId;
    }

    [Serializable]
    public sealed class QueueLeaveMatchResponse
    {
        public bool success;
        public string status;
    }

    [Serializable]
    public sealed class MatchPresencePositionDto
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public sealed class MatchPresenceUpdateRequest
    {
        public string ticketId;
        public MatchPresencePositionDto position;
        public float yaw;
        public long sampleTimeMs;
    }

    [Serializable]
    public sealed class MatchPresenceUpdateResponse
    {
        public bool success;
    }

    [Serializable]
    public sealed class MatchPresencePlayerDto
    {
        public string ticketId;
        public MatchPresencePositionDto position;
        public float yaw;
        public int sampleTick;
        public long sampleTimeMs;
    }

    [Serializable]
    public sealed class MatchPresenceSnapshotResponse
    {
        public bool success;
        public long serverTimeMs;
        public int serverTick;
        public int serverTickRate;
        public MatchPresencePlayerDto[] players;
    }

    [Serializable]
    public sealed class MatchPresenceSyncResponse
    {
        public bool success;
        public long serverTimeMs;
        public int serverTick;
        public int serverTickRate;
        public MatchPresencePlayerDto[] players;
    }

    public sealed class QueueApiClient : MonoBehaviour
    {
        [SerializeField] private string baseUrl = "http://127.0.0.1:5050";
        [SerializeField] private float requestTimeoutSeconds = 5f;

        public void Configure(string apiBaseUrl, float timeoutSeconds)
        {
            if (!string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                baseUrl = apiBaseUrl.TrimEnd('/');
            }

            requestTimeoutSeconds = Mathf.Max(1f, timeoutSeconds);
        }

        public IEnumerator Enqueue(
            string playerId,
            Action<bool, QueueEnqueueResponse, string> onCompleted)
        {
            var requestBody = new QueueEnqueueRequest
            {
                playerId = playerId
            };

            yield return SendRequest(
                method: UnityWebRequest.kHttpVerbPOST,
                path: "/enqueue",
                requestBody: requestBody,
                onCompleted: (ok, responseJson, error) =>
                {
                    if (!ok)
                    {
                        onCompleted?.Invoke(false, null, error);
                        return;
                    }

                    var response = ParseJson<QueueEnqueueResponse>(responseJson);
                    if (response == null || string.IsNullOrWhiteSpace(response.ticketId))
                    {
                        onCompleted?.Invoke(false, null, "Invalid enqueue response.");
                        return;
                    }

                    onCompleted?.Invoke(true, response, string.Empty);
                });
        }

        public IEnumerator Dequeue(
            string ticketId,
            Action<bool, QueueDequeueResponse, string> onCompleted)
        {
            var requestBody = new QueueDequeueRequest
            {
                ticketId = ticketId
            };

            yield return SendRequest(
                method: UnityWebRequest.kHttpVerbPOST,
                path: "/dequeue",
                requestBody: requestBody,
                onCompleted: (ok, responseJson, error) =>
                {
                    if (!ok)
                    {
                        onCompleted?.Invoke(false, null, error);
                        return;
                    }

                    var response = ParseJson<QueueDequeueResponse>(responseJson);
                    if (response == null)
                    {
                        onCompleted?.Invoke(false, null, "Invalid dequeue response.");
                        return;
                    }

                    onCompleted?.Invoke(true, response, string.Empty);
                });
        }

        public IEnumerator GetTicketStatus(
            string ticketId,
            Action<bool, QueueTicketStatusResponse, string> onCompleted)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                onCompleted?.Invoke(false, null, "Ticket id is empty.");
                yield break;
            }

            var path = $"/ticket/{UnityWebRequest.EscapeURL(ticketId)}";
            yield return SendRequest(
                method: UnityWebRequest.kHttpVerbGET,
                path: path,
                requestBody: null,
                onCompleted: (ok, responseJson, error) =>
                {
                    if (!ok)
                    {
                        onCompleted?.Invoke(false, null, error);
                        return;
                    }

                    var response = ParseJson<QueueTicketStatusResponse>(responseJson);
                    if (response == null || string.IsNullOrWhiteSpace(response.status))
                    {
                        onCompleted?.Invoke(false, null, "Invalid ticket status response.");
                        return;
                    }

                    onCompleted?.Invoke(true, response, string.Empty);
                });
        }

        public IEnumerator LeaveMatch(
            string ticketId,
            Action<bool, QueueLeaveMatchResponse, string> onCompleted)
        {
            var requestBody = new QueueLeaveMatchRequest
            {
                ticketId = ticketId
            };

            yield return SendRequest(
                method: UnityWebRequest.kHttpVerbPOST,
                path: "/match/leave",
                requestBody: requestBody,
                onCompleted: (ok, responseJson, error) =>
                {
                    if (!ok)
                    {
                        onCompleted?.Invoke(false, null, error);
                        return;
                    }

                    var response = ParseJson<QueueLeaveMatchResponse>(responseJson);
                    if (response == null)
                    {
                        onCompleted?.Invoke(false, null, "Invalid leave match response.");
                        return;
                    }

                    onCompleted?.Invoke(true, response, string.Empty);
                });
        }

        public IEnumerator UpdateMatchPresence(
            string ticketId,
            Vector3 position,
            float yaw,
            long sampleTimeMs,
            Action<bool, MatchPresenceUpdateResponse, string> onCompleted)
        {
            var requestBody = new MatchPresenceUpdateRequest
            {
                ticketId = ticketId,
                position = new MatchPresencePositionDto
                {
                    x = position.x,
                    y = position.y,
                    z = position.z
                },
                yaw = yaw,
                sampleTimeMs = sampleTimeMs
            };

            yield return SendRequest(
                method: UnityWebRequest.kHttpVerbPOST,
                path: "/match/presence/update",
                requestBody: requestBody,
                onCompleted: (ok, responseJson, error) =>
                {
                    if (!ok)
                    {
                        onCompleted?.Invoke(false, null, error);
                        return;
                    }

                    var response = ParseJson<MatchPresenceUpdateResponse>(responseJson);
                    if (response == null)
                    {
                        onCompleted?.Invoke(false, null, "Invalid presence update response.");
                        return;
                    }

                    onCompleted?.Invoke(true, response, string.Empty);
                });
        }

        public IEnumerator GetMatchPresence(
            string ticketId,
            Action<bool, MatchPresenceSnapshotResponse, string> onCompleted)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                onCompleted?.Invoke(false, null, "Ticket id is empty.");
                yield break;
            }

            var path = $"/match/presence/{UnityWebRequest.EscapeURL(ticketId)}";
            yield return SendRequest(
                method: UnityWebRequest.kHttpVerbGET,
                path: path,
                requestBody: null,
                onCompleted: (ok, responseJson, error) =>
                {
                    if (!ok)
                    {
                        onCompleted?.Invoke(false, null, error);
                        return;
                    }

                    var response = ParseJson<MatchPresenceSnapshotResponse>(responseJson);
                    if (response == null)
                    {
                        onCompleted?.Invoke(false, null, "Invalid match presence response.");
                        return;
                    }

                    onCompleted?.Invoke(true, response, string.Empty);
                });
        }

        public IEnumerator SyncMatchPresence(
            string ticketId,
            Vector3 position,
            float yaw,
            long sampleTimeMs,
            Action<bool, MatchPresenceSyncResponse, string> onCompleted)
        {
            var requestBody = new MatchPresenceUpdateRequest
            {
                ticketId = ticketId,
                position = new MatchPresencePositionDto
                {
                    x = position.x,
                    y = position.y,
                    z = position.z
                },
                yaw = yaw,
                sampleTimeMs = sampleTimeMs
            };

            yield return SendRequest(
                method: UnityWebRequest.kHttpVerbPOST,
                path: "/match/presence/sync",
                requestBody: requestBody,
                onCompleted: (ok, responseJson, error) =>
                {
                    if (!ok)
                    {
                        onCompleted?.Invoke(false, null, error);
                        return;
                    }

                    var response = ParseJson<MatchPresenceSyncResponse>(responseJson);
                    if (response == null)
                    {
                        onCompleted?.Invoke(false, null, "Invalid match presence sync response.");
                        return;
                    }

                    onCompleted?.Invoke(true, response, string.Empty);
                });
        }

        private IEnumerator SendRequest(
            string method,
            string path,
            object requestBody,
            Action<bool, string, string> onCompleted)
        {
            var url = BuildUrl(path);
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, Mathf.RoundToInt(requestTimeoutSeconds));

                if (requestBody != null)
                {
                    var payloadJson = JsonUtility.ToJson(requestBody);
                    var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                    request.uploadHandler = new UploadHandlerRaw(payloadBytes);
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                yield return request.SendWebRequest();

                var responseText = request.downloadHandler != null
                    ? request.downloadHandler.text
                    : string.Empty;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    var status = request.responseCode > 0 ? $"HTTP {request.responseCode}. " : string.Empty;
                    var error = $"{status}{request.error}".Trim();
                    if (!string.IsNullOrWhiteSpace(responseText))
                    {
                        error = $"{error} {responseText}".Trim();
                    }

                    onCompleted?.Invoke(false, string.Empty, error);
                    yield break;
                }

                onCompleted?.Invoke(true, responseText, string.Empty);
            }
        }

        private string BuildUrl(string path)
        {
            var normalizedBase = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://127.0.0.1:5050"
                : baseUrl.TrimEnd('/');

            if (string.IsNullOrWhiteSpace(path))
            {
                return normalizedBase;
            }

            return path[0] == '/'
                ? $"{normalizedBase}{path}"
                : $"{normalizedBase}/{path}";
        }

        private static T ParseJson<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
