using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ShooterPrototype.Network
{
    internal static class RealtimeSnapshotBinaryCodec
    {
        private static readonly byte[] Magic = { (byte)'R', (byte)'T', (byte)'S', (byte)'1' };

        public static bool TryDecode(byte[] data, out RealtimeTransportClient.RealtimeSnapshot snapshot)
        {
            snapshot = null;
            if (data == null || data.Length < 12)
            {
                return false;
            }

            for (var i = 0; i < Magic.Length; i++)
            {
                if (data[i] != Magic[i])
                {
                    return false;
                }
            }

            var offset = 4;
            var version = ReadU8(data, ref offset);
            if (version != 1 && version != 2)
            {
                return false;
            }

            var serverTick = ReadU32(data, ref offset);
            var serverTickRate = ReadU16(data, ref offset);
            var flags = ReadU8(data, ref offset);

            RealtimeTransportClient.SelfAuthoritativePose selfAuth = null;
            if ((flags & 1) != 0)
            {
                if (offset + 20 > data.Length)
                {
                    return false;
                }

                var sampleTick = ReadU32(data, ref offset);
                var x = ReadF32(data, ref offset);
                var y = ReadF32(data, ref offset);
                var z = ReadF32(data, ref offset);
                var yaw = ReadF32(data, ref offset);
                selfAuth = new RealtimeTransportClient.SelfAuthoritativePose
                {
                    sampleTick = (int)sampleTick,
                    yaw = yaw,
                    position = new RealtimeTransportClient.PositionDto { x = x, y = y, z = z }
                };
            }

            if (offset >= data.Length)
            {
                return false;
            }

            var playerCount = ReadU8(data, ref offset);
            var players = new List<RealtimeTransportClient.RealtimePlayerState>(playerCount);
            for (var i = 0; i < playerCount; i++)
            {
                if (offset >= data.Length)
                {
                    return false;
                }

                var ticketLen = ReadU8(data, ref offset);
                if (offset + ticketLen > data.Length)
                {
                    return false;
                }

                var ticketId = Encoding.UTF8.GetString(data, offset, ticketLen);
                offset += ticketLen;

                if (offset + 35 > data.Length)
                {
                    return false;
                }

                var sampleTick = (int)ReadU32(data, ref offset);
                var px = ReadF32(data, ref offset);
                var py = ReadF32(data, ref offset);
                var pz = ReadF32(data, ref offset);
                var yaw = ReadF32(data, ref offset);
                var velX = ReadF32(data, ref offset);
                var velY = ReadF32(data, ref offset);
                var velZ = ReadF32(data, ref offset);
                var flags2 = ReadU16(data, ref offset);
                var jumpState = ReadU8(data, ref offset);

                if (offset + (version >= 2 ? 72 : 48) > data.Length)
                {
                    return false;
                }

                var lookPitch = ReadF32(data, ref offset);
                var shotSeq = (int)ReadU32(data, ref offset);
                var reloadSeq = (int)ReadU32(data, ref offset);
                var hitPlayerSeq = (int)ReadU32(data, ref offset);
                var footstepSeq = (int)ReadU32(data, ref offset);
                var wallAvoidBlend = ReadF32(data, ref offset);
                var animSpeed = ReadF32(data, ref offset);
                var animPhase = ReadF32(data, ref offset);
                var deathSeq = (int)ReadU32(data, ref offset);
                var deathFallDirX = ReadF32(data, ref offset);
                var deathFallDirY = ReadF32(data, ref offset);
                var deathFallDirZ = ReadF32(data, ref offset);
                var shotOriginX = 0f;
                var shotOriginY = 0f;
                var shotOriginZ = 0f;
                var shotDirX = 0f;
                var shotDirY = 0f;
                var shotDirZ = 0f;
                if (version >= 2)
                {
                    if (offset + 24 > data.Length)
                    {
                        return false;
                    }

                    shotOriginX = ReadF32(data, ref offset);
                    shotOriginY = ReadF32(data, ref offset);
                    shotOriginZ = ReadF32(data, ref offset);
                    shotDirX = ReadF32(data, ref offset);
                    shotDirY = ReadF32(data, ref offset);
                    shotDirZ = ReadF32(data, ref offset);
                }

                if (offset >= data.Length)
                {
                    return false;
                }

                var historyCount = ReadU8(data, ref offset);
                RealtimeTransportClient.RealtimeStateSample[] history = null;
                if (historyCount > 0)
                {
                    history = new RealtimeTransportClient.RealtimeStateSample[historyCount];
                    for (var h = 0; h < historyCount; h++)
                    {
                        if (offset + 32 > data.Length)
                        {
                            return false;
                        }

                        history[h] = new RealtimeTransportClient.RealtimeStateSample
                        {
                            sampleTick = (int)ReadU32(data, ref offset),
                            x = ReadF32(data, ref offset),
                            y = ReadF32(data, ref offset),
                            z = ReadF32(data, ref offset),
                            yaw = ReadF32(data, ref offset),
                            velX = ReadF32(data, ref offset),
                            velY = ReadF32(data, ref offset),
                            velZ = ReadF32(data, ref offset)
                        };
                    }
                }

                players.Add(new RealtimeTransportClient.RealtimePlayerState
                {
                    ticketId = ticketId,
                    position = new RealtimeTransportClient.PositionDto { x = px, y = py, z = pz },
                    yaw = yaw,
                    velX = velX,
                    velY = velY,
                    velZ = velZ,
                    lookPitch = lookPitch,
                    shotSeq = shotSeq,
                    reloadSeq = reloadSeq,
                    hitPlayerSeq = hitPlayerSeq,
                    footstepSeq = footstepSeq,
                    wallAvoidBlend = wallAvoidBlend,
                    animSpeed = animSpeed,
                    animPhase = animPhase,
                    deathSeq = deathSeq,
                    deathFallDirX = deathFallDirX,
                    deathFallDirY = deathFallDirY,
                    deathFallDirZ = deathFallDirZ,
                    shotOriginX = shotOriginX,
                    shotOriginY = shotOriginY,
                    shotOriginZ = shotOriginZ,
                    shotDirX = shotDirX,
                    shotDirY = shotDirY,
                    shotDirZ = shotDirZ,
                    isDead = (flags2 & 1) != 0,
                    isGrounded = (flags2 & 2) != 0,
                    isCrouching = (flags2 & 4) != 0,
                    isSprinting = (flags2 & 8) != 0,
                    isAiming = (flags2 & 16) != 0,
                    jumpState = jumpState,
                    sampleTick = sampleTick,
                    history = history
                });
            }

            snapshot = new RealtimeTransportClient.RealtimeSnapshot
            {
                type = "snapshot",
                serverTick = (int)serverTick,
                serverTickRate = serverTickRate,
                players = players.ToArray(),
                selfAuthoritative = selfAuth
            };
            return true;
        }

        private static byte ReadU8(byte[] data, ref int offset)
        {
            return data[offset++];
        }

        private static ushort ReadU16(byte[] data, ref int offset)
        {
            var value = BitConverter.ToUInt16(data, offset);
            offset += 2;
            return value;
        }

        private static uint ReadU32(byte[] data, ref int offset)
        {
            var value = BitConverter.ToUInt32(data, offset);
            offset += 4;
            return value;
        }

        private static float ReadF32(byte[] data, ref int offset)
        {
            var value = BitConverter.ToSingle(data, offset);
            offset += 4;
            return value;
        }
    }
}
