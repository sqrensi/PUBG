using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Matchmaking
{
    public enum MatchTicketStatus
    {
        Queued = 0,
        Matched = 1,
        Cancelled = 2,
        Expired = 3
    }

    public struct MatchTicketSnapshot
    {
        public string TicketId;
        public string PlayerId;
        public MatchTicketStatus Status;
        public float QueueDurationSeconds;
        public string ServerAddress;
        public int ServerPort;
    }

    public sealed class QueueServiceMock : MonoBehaviour
    {
        private sealed class TicketRecord
        {
            public string TicketId;
            public string PlayerId;
            public float EnqueuedAt;
            public MatchTicketStatus Status;
            public string ServerAddress;
            public int ServerPort;
        }

        [Header("Matchmaking")]
        [SerializeField] private int minPlayersToMatch = 1;
        [SerializeField] private float enqueueToMatchDelaySeconds = 2f;
        [SerializeField] private float queueTimeoutSeconds = 20f;

        [Header("Dedicated Server Endpoint")]
        [SerializeField] private string serverAddress = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;

        private readonly Dictionary<string, TicketRecord> ticketsById = new Dictionary<string, TicketRecord>();
        private readonly List<TicketRecord> queuedTickets = new List<TicketRecord>();

        public string Enqueue(string playerId)
        {
            var ticketId = Guid.NewGuid().ToString("N");
            var record = new TicketRecord
            {
                TicketId = ticketId,
                PlayerId = string.IsNullOrWhiteSpace(playerId) ? "anonymous" : playerId,
                EnqueuedAt = Time.unscaledTime,
                Status = MatchTicketStatus.Queued,
                ServerAddress = string.Empty,
                ServerPort = 0
            };

            ticketsById[ticketId] = record;
            queuedTickets.Add(record);

            Debug.Log($"[QueueServiceMock] Enqueue ticket={ticketId}, player={record.PlayerId}");
            return ticketId;
        }

        public bool Dequeue(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return false;
            }

            if (!ticketsById.TryGetValue(ticketId, out var record))
            {
                return false;
            }

            if (record.Status != MatchTicketStatus.Queued)
            {
                return false;
            }

            record.Status = MatchTicketStatus.Cancelled;
            queuedTickets.Remove(record);

            Debug.Log($"[QueueServiceMock] Dequeue ticket={ticketId}");
            return true;
        }

        public MatchTicketSnapshot GetTicketStatus(string ticketId)
        {
            if (!ticketsById.TryGetValue(ticketId, out var record))
            {
                return new MatchTicketSnapshot
                {
                    TicketId = ticketId,
                    PlayerId = string.Empty,
                    Status = MatchTicketStatus.Expired,
                    QueueDurationSeconds = 0f,
                    ServerAddress = string.Empty,
                    ServerPort = 0
                };
            }

            return new MatchTicketSnapshot
            {
                TicketId = record.TicketId,
                PlayerId = record.PlayerId,
                Status = record.Status,
                QueueDurationSeconds = Mathf.Max(0f, Time.unscaledTime - record.EnqueuedAt),
                ServerAddress = record.ServerAddress,
                ServerPort = record.ServerPort
            };
        }

        private void Update()
        {
            if (queuedTickets.Count == 0)
            {
                return;
            }

            ExpireTimedOutTickets();
            TryCreateMatch();
        }

        private void ExpireTimedOutTickets()
        {
            if (queueTimeoutSeconds <= 0f)
            {
                return;
            }

            var now = Time.unscaledTime;
            for (var i = queuedTickets.Count - 1; i >= 0; i--)
            {
                var ticket = queuedTickets[i];
                var queuedFor = now - ticket.EnqueuedAt;
                if (queuedFor < queueTimeoutSeconds)
                {
                    continue;
                }

                ticket.Status = MatchTicketStatus.Expired;
                queuedTickets.RemoveAt(i);
                Debug.Log($"[QueueServiceMock] Ticket expired ticket={ticket.TicketId}");
            }
        }

        private void TryCreateMatch()
        {
            var requiredPlayers = Mathf.Max(1, minPlayersToMatch);
            if (queuedTickets.Count < requiredPlayers)
            {
                return;
            }

            var oldestTicket = queuedTickets[0];
            var waitedSeconds = Time.unscaledTime - oldestTicket.EnqueuedAt;
            if (waitedSeconds < Mathf.Max(0f, enqueueToMatchDelaySeconds))
            {
                return;
            }

            var matchedPlayers = Mathf.Min(requiredPlayers, queuedTickets.Count);
            for (var i = 0; i < matchedPlayers; i++)
            {
                var ticket = queuedTickets[0];
                queuedTickets.RemoveAt(0);

                ticket.Status = MatchTicketStatus.Matched;
                ticket.ServerAddress = serverAddress;
                ticket.ServerPort = serverPort;

                Debug.Log(
                    $"[QueueServiceMock] Ticket matched ticket={ticket.TicketId}, endpoint={ticket.ServerAddress}:{ticket.ServerPort}");
            }
        }
    }
}
