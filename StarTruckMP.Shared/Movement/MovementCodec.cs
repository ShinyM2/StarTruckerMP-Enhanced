using System;
using System.Buffers.Binary;

namespace StarTruckMP.Shared.Movement
{
    /// <summary>
    /// The movement packet, by hand rather than through MessagePack.
    ///
    /// Movement is the one thing that goes out twenty-five times a second to everyone in the
    /// sector, so its size is the mod's bandwidth. MessagePack spends five bytes on every float
    /// and a header on every array; laid out by hand a body is 31 bytes: the position as three
    /// floats, because world coordinates need the precision, the rotation as its three smallest
    /// components in sixteen bits each, and the velocities as half floats, whose worst error at
    /// truck speeds is a few millimetres over one send interval.
    ///
    /// The layout, after the packet type byte:
    /// <code>
    ///   byte   format            (1)
    ///   uint32 seq
    ///   int64  sentAt            milliseconds of the sender's clock
    ///   byte   historyCount      0..MaxHistory
    ///   entry  current           byte bodies (bit 0 cab, bit 1 trailer), then each body present
    ///   entry  history[i]        uint16 msBefore, byte bodies, bodies...
    ///                            seq = seq - i - 1, sentAt = sentAt - msBefore
    /// </code>
    /// The server relays the client's bytes untouched behind a four-byte net id
    /// (<see cref="WriteRelayed"/>), so it never re-serialises what it does not read.
    /// </summary>
    public static class MovementCodec
    {
        public const byte Format = 1;

        /// <summary>The most earlier entries a packet may repeat.</summary>
        public const int MaxHistory = 3;

        private const int BodyBytes = 31;
        private const int HeaderBytes = 1 + 4 + 8 + 1;

        /// <summary>The largest payload a packet can legitimately have, after the type byte.</summary>
        public const int MaxPayloadBytes = HeaderBytes + (1 + 2 * BodyBytes) + MaxHistory * (2 + 1 + 2 * BodyBytes);

        // -----------------------------------------------------------------------------------
        // Writing
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Writes a packet payload for <paramref name="current"/> repeating <paramref name="history"/>,
        /// newest first, with consecutive sequence numbers just below the current one. Returns the
        /// number of bytes written; <paramref name="buffer"/> must hold <see cref="MaxPayloadBytes"/>.
        /// </summary>
        public static int Write(Span<byte> buffer, in MovementEntry current, ReadOnlySpan<MovementEntry> history)
        {
            var count = Math.Min(history.Length, MaxHistory);

            buffer[0] = Format;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(1), current.Seq);
            BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(5), current.SentAt);
            buffer[13] = (byte)count;

            var offset = HeaderBytes;
            offset += WriteEntry(buffer.Slice(offset), current);

            for (var i = 0; i < count; i++)
            {
                var before = current.SentAt - history[i].SentAt;
                if (before < 0) before = 0;
                if (before > ushort.MaxValue) before = ushort.MaxValue;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset), (ushort)before);
                offset += 2;
                offset += WriteEntry(buffer.Slice(offset), history[i]);
            }

            return offset;
        }

        private static int WriteEntry(Span<byte> buffer, in MovementEntry entry)
        {
            buffer[0] = (byte)((entry.HasCab ? 1 : 0) | (entry.HasTrailer ? 2 : 0));
            var offset = 1;
            if (entry.HasCab) offset += WriteBody(buffer.Slice(offset), entry.Cab);
            if (entry.HasTrailer) offset += WriteBody(buffer.Slice(offset), entry.Trailer);
            return offset;
        }

        private static int WriteBody(Span<byte> b, in BodyState body)
        {
            BinaryPrimitives.WriteInt32LittleEndian(b.Slice(0), BitConverter.SingleToInt32Bits(body.Position.X));
            BinaryPrimitives.WriteInt32LittleEndian(b.Slice(4), BitConverter.SingleToInt32Bits(body.Position.Y));
            BinaryPrimitives.WriteInt32LittleEndian(b.Slice(8), BitConverter.SingleToInt32Bits(body.Position.Z));
            WriteRotation(b.Slice(12), body.Rotation);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(19), HalfFloat.FromSingle(body.Velocity.X));
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(21), HalfFloat.FromSingle(body.Velocity.Y));
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(23), HalfFloat.FromSingle(body.Velocity.Z));
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(25), HalfFloat.FromSingle(body.AngVel.X));
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(27), HalfFloat.FromSingle(body.AngVel.Y));
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(29), HalfFloat.FromSingle(body.AngVel.Z));
            return BodyBytes;
        }

        /// <summary>The server's framing: the sender's net id in front of the client's payload, untouched.</summary>
        public static byte[] WriteRelayed(PacketType type, int netId, ReadOnlySpan<byte> clientPayload)
        {
            var packet = new byte[1 + 4 + clientPayload.Length];
            packet[0] = (byte)type;
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(1), netId);
            clientPayload.CopyTo(packet.AsSpan(5));
            return packet;
        }

        // -----------------------------------------------------------------------------------
        // Reading
        // -----------------------------------------------------------------------------------

        /// <summary>Splits a relayed payload into the sender and the client's own bytes.</summary>
        public static bool TryReadRelayed(ReadOnlySpan<byte> payload, out int netId, out ReadOnlySpan<byte> clientPayload)
        {
            if (payload.Length < 4)
            {
                netId = -1;
                clientPayload = default;
                return false;
            }

            netId = BinaryPrimitives.ReadInt32LittleEndian(payload);
            clientPayload = payload.Slice(4);
            return true;
        }

        /// <summary>
        /// Reads a client payload. <paramref name="history"/> must have room for <see cref="MaxHistory"/>
        /// entries; <paramref name="historyCount"/> says how many were filled. False for anything
        /// malformed, which a relay should drop rather than pass on.
        /// </summary>
        public static bool TryRead(ReadOnlySpan<byte> payload, out MovementEntry current, Span<MovementEntry> history, out int historyCount)
        {
            current = default;
            historyCount = 0;

            if (payload.Length < HeaderBytes + 1 || payload.Length > MaxPayloadBytes) return false;
            if (payload[0] != Format) return false;

            var seq = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1));
            var sentAt = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(5));
            int count = payload[13];
            if (count > MaxHistory || count > history.Length) return false;

            var offset = HeaderBytes;
            if (!TryReadEntry(payload, ref offset, out current)) return false;
            current.Seq = seq;
            current.SentAt = sentAt;

            for (var i = 0; i < count; i++)
            {
                if (offset + 2 > payload.Length) return false;
                var before = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset));
                offset += 2;

                if (!TryReadEntry(payload, ref offset, out var past)) return false;
                past.Seq = unchecked(seq - (uint)(i + 1));
                past.SentAt = sentAt - before;
                history[i] = past;
            }

            historyCount = count;
            return offset == payload.Length;
        }

        /// <summary>Only what a relay needs: the counter, the clock and the current cab state.</summary>
        public static bool TryReadCurrent(ReadOnlySpan<byte> payload, out MovementEntry current)
        {
            current = default;
            if (payload.Length < HeaderBytes + 1 || payload.Length > MaxPayloadBytes) return false;
            if (payload[0] != Format) return false;
            if (payload[13] > MaxHistory) return false;

            var offset = HeaderBytes;
            if (!TryReadEntry(payload, ref offset, out current)) return false;
            current.Seq = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1));
            current.SentAt = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(5));
            return true;
        }

        private static bool TryReadEntry(ReadOnlySpan<byte> payload, ref int offset, out MovementEntry entry)
        {
            entry = default;
            if (offset >= payload.Length) return false;

            var bodies = payload[offset++];
            if ((bodies & ~3) != 0) return false;

            entry.HasCab = (bodies & 1) != 0;
            entry.HasTrailer = (bodies & 2) != 0;

            if (entry.HasCab)
            {
                if (offset + BodyBytes > payload.Length) return false;
                entry.Cab = ReadBody(payload.Slice(offset));
                offset += BodyBytes;
            }

            if (entry.HasTrailer)
            {
                if (offset + BodyBytes > payload.Length) return false;
                entry.Trailer = ReadBody(payload.Slice(offset));
                offset += BodyBytes;
            }

            return true;
        }

        private static BodyState ReadBody(ReadOnlySpan<byte> b)
        {
            return new BodyState
            {
                Position = new Vector3
                {
                    X = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.Slice(0))),
                    Y = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.Slice(4))),
                    Z = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.Slice(8)))
                },
                Rotation = ReadRotation(b.Slice(12)),
                Velocity = new Vector3
                {
                    X = HalfFloat.ToSingle(BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(19))),
                    Y = HalfFloat.ToSingle(BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(21))),
                    Z = HalfFloat.ToSingle(BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(23)))
                },
                AngVel = new Vector3
                {
                    X = HalfFloat.ToSingle(BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(25))),
                    Y = HalfFloat.ToSingle(BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(27))),
                    Z = HalfFloat.ToSingle(BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(29)))
                }
            };
        }

        // -----------------------------------------------------------------------------------
        // Rotation: the three smallest components, sixteen bits each
        // -----------------------------------------------------------------------------------

        /// <summary>A unit quaternion's components other than the largest lie within ±1/√2.</summary>
        private const float ComponentRange = 0.70710678f;
        private const float ComponentScale = 32767f / ComponentRange;

        /// <summary>Seven bytes: which component was largest, then the other three as signed sixteen-bit fractions of ±1/√2.</summary>
        private static void WriteRotation(Span<byte> b, Quaternion q)
        {
            var x = q.X;
            var y = q.Y;
            var z = q.Z;
            var w = q.W;

            // Normalise defensively: a rotation read straight off a rigidbody is unit length, but a
            // stray NaN or a zero quaternion must not poison every packet after it.
            var length = MathF.Sqrt(x * x + y * y + z * z + w * w);
            if (!(length > 1e-6f) || float.IsNaN(length))
            {
                x = 0; y = 0; z = 0; w = 1; length = 1;
            }
            x /= length; y /= length; z /= length; w /= length;

            var largest = 3;
            var largestAbs = MathF.Abs(w);
            if (MathF.Abs(x) > largestAbs) { largest = 0; largestAbs = MathF.Abs(x); }
            if (MathF.Abs(y) > largestAbs) { largest = 1; largestAbs = MathF.Abs(y); }
            if (MathF.Abs(z) > largestAbs) { largest = 2; }

            // q and -q are the same rotation: pick the sign that makes the dropped component positive,
            // so the reader can rebuild it as a plain square root.
            var dropped = largest switch { 0 => x, 1 => y, 2 => z, _ => w };
            var sign = dropped < 0 ? -1f : 1f;
            x *= sign; y *= sign; z *= sign; w *= sign;

            b[0] = (byte)largest;
            var offset = 1;
            if (largest != 0) { WriteComponent(b.Slice(offset), x); offset += 2; }
            if (largest != 1) { WriteComponent(b.Slice(offset), y); offset += 2; }
            if (largest != 2) { WriteComponent(b.Slice(offset), z); offset += 2; }
            if (largest != 3) { WriteComponent(b.Slice(offset), w); }
        }

        private static void WriteComponent(Span<byte> b, float value)
        {
            var scaled = MathF.Round(value * ComponentScale);
            if (scaled > short.MaxValue) scaled = short.MaxValue;
            if (scaled < short.MinValue) scaled = short.MinValue;
            BinaryPrimitives.WriteInt16LittleEndian(b, (short)scaled);
        }

        private static Quaternion ReadRotation(ReadOnlySpan<byte> b)
        {
            var largest = b[0];
            if (largest > 3) return new Quaternion { W = 1f };

            var a = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(1)) / ComponentScale;
            var c = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(3)) / ComponentScale;
            var d = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(5)) / ComponentScale;

            var rest = 1f - (a * a + c * c + d * d);
            var big = rest > 0f ? MathF.Sqrt(rest) : 0f;

            return largest switch
            {
                0 => new Quaternion { X = big, Y = a, Z = c, W = d },
                1 => new Quaternion { X = a, Y = big, Z = c, W = d },
                2 => new Quaternion { X = a, Y = c, Z = big, W = d },
                _ => new Quaternion { X = a, Y = c, Z = d, W = big }
            };
        }
    }

    /// <summary>
    /// IEEE half precision by hand: the shared assembly targets netstandard2.1, which has no
    /// <c>System.Half</c>. Round to nearest even, like the hardware does.
    /// </summary>
    public static class HalfFloat
    {
        public static ushort FromSingle(float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            var sign = (bits >> 16) & 0x8000;
            var exponent = ((bits >> 23) & 0xFF) - 127 + 15;
            var mantissa = bits & 0x7FFFFF;

            if (((bits >> 23) & 0xFF) == 0xFF)
            {
                // Infinity or NaN: keep the class, drop the payload.
                return (ushort)(sign | 0x7C00 | (mantissa != 0 ? 0x200 : 0));
            }

            if (exponent >= 0x1F) return (ushort)(sign | 0x7C00); // overflow to infinity

            if (exponent <= 0)
            {
                // Subnormal or zero in half precision.
                if (exponent < -10) return (ushort)sign;
                mantissa |= 0x800000;
                var shift = 14 - exponent;
                var half = mantissa >> shift;
                var remainder = mantissa & ((1 << shift) - 1);
                var halfway = 1 << (shift - 1);
                if (remainder > halfway || (remainder == halfway && (half & 1) != 0)) half++;
                return (ushort)(sign | half);
            }

            var result = sign | (exponent << 10) | (mantissa >> 13);
            var rest = mantissa & 0x1FFF;
            if (rest > 0x1000 || (rest == 0x1000 && (result & 1) != 0)) result++;
            return (ushort)result;
        }

        public static float ToSingle(ushort half)
        {
            var sign = (half & 0x8000) << 16;
            var exponent = (half >> 10) & 0x1F;
            var mantissa = half & 0x3FF;

            if (exponent == 0)
            {
                if (mantissa == 0) return BitConverter.Int32BitsToSingle(sign);

                // Subnormal: normalise.
                exponent = 1;
                while ((mantissa & 0x400) == 0)
                {
                    mantissa <<= 1;
                    exponent--;
                }
                mantissa &= 0x3FF;
            }
            else if (exponent == 0x1F)
            {
                return BitConverter.Int32BitsToSingle(sign | 0x7F800000 | (mantissa << 13));
            }

            return BitConverter.Int32BitsToSingle(sign | ((exponent + 127 - 15) << 23) | (mantissa << 13));
        }
    }
}
