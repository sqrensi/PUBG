using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class PlayerNetworkIdentity : MonoBehaviour
    {
        [SerializeField] private string ticketId = string.Empty;
        [SerializeField] private bool isLocalPlayer;

        public string TicketId => ticketId;
        public bool IsLocalPlayer => isLocalPlayer;

        public void Configure(string networkTicketId, bool localPlayer)
        {
            ticketId = string.IsNullOrWhiteSpace(networkTicketId) ? string.Empty : networkTicketId.Trim();
            isLocalPlayer = localPlayer;
        }
    }
}
