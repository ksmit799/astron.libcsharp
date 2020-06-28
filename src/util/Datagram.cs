using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using astron.core;

namespace astron.util
{
    public class Datagram
    {
        // Default capacity for a newly constructed packet.
        // Higher value - Greater memory usage, less copying.
        // Lower value - Less memory usage, more copying.
        protected static readonly int DefaultCapacity = 16;

        protected byte[] data;
        protected int size;
        protected int capacity;

        public Datagram()
        {
            data = new byte[DefaultCapacity];
            size = 0;
            capacity = DefaultCapacity;
        }

        public Datagram(MsgTypes msgType) : this()
        {
            WriteUint16((ushort)msgType);
        }

        public Datagram(byte[] dataArr, int dataSize)
        {
            data = dataArr;
            size = dataSize;
            capacity = dataSize;
        }

        public void WriteServerHeader(ulong channel, ulong sender, MsgTypes msgType)
        {
            WriteInt8(1);
            WriteChannel(channel);
            WriteChannel(sender);
            WriteUint16((ushort)msgType);
        }

        public void WriteServerControlHeader(MsgTypes msgType)
        {
            WriteInt8(1);
            WriteChannel((ulong)MsgTypes.CONTROL_CHANNEL);
            WriteUint16((ushort)msgType);
        }

        public void ResizeBuffer(int growthSize)
        {
            int newSize = size + growthSize;

            // If the new size is greater then the current capacity,
            // double the existing buffer.
            if (newSize > capacity)
            {
                // Widen the buffer by a factor of two.
                capacity *= 2;
                if (capacity < newSize)
                {
                    ResizeBuffer(growthSize);
                    return;
                }

                Array.Resize(ref data, capacity);
            }
        }

        public void Write(byte val)
        {
            Debug.Assert(size + 1 <= capacity);
            data[size++] = val;
        }

        public void WriteInt8(sbyte val)
        {
            ResizeBuffer(1);
            Write((byte)val);
        }

        public void WriteUint8(byte val)
        {
            ResizeBuffer(1);
            Write(val);
        }

        public void WriteInt16(short val)
        {
            ResizeBuffer(2);
            Write((byte)val);
            Write((byte)(val >> 8));
        }

        public void WriteUint16(ushort val)
        {
            ResizeBuffer(2);
            Write((byte)val);
            Write((byte)(val >> 8));
        }

        public void WriteInt32(int val)
        {
            ResizeBuffer(4);
            Write((byte)val);
            Write((byte)(val >> 8));
            Write((byte)(val >> 16));
            Write((byte)(val >> 24));
        }

        public void WriteUint32(uint val)
        {
            ResizeBuffer(4);
            Write((byte)val);
            Write((byte)(val >> 8));
            Write((byte)(val >> 16));
            Write((byte)(val >> 24));
        }

        public void WriteInt64(long val)
        {
            ResizeBuffer(8);
            Write((byte)val);
            Write((byte)(val >> 8));
            Write((byte)(val >> 16));
            Write((byte)(val >> 24));
            Write((byte)(val >> 32));
            Write((byte)(val >> 40));
            Write((byte)(val >> 48));
            Write((byte)(val >> 56));
        }

        public void WriteUint64(ulong val)
        {
            ResizeBuffer(8);
            Write((byte)val);
            Write((byte)(val >> 8));
            Write((byte)(val >> 16));
            Write((byte)(val >> 24));
            Write((byte)(val >> 32));
            Write((byte)(val >> 40));
            Write((byte)(val >> 48));
            Write((byte)(val >> 56));
        }

        public void WriteString(string val)
        {
            if (val.Length > ushort.MaxValue)
            {
                throw new IOException("String larger then max allowed size");
            }

            // Write the string length.
            WriteUint16((ushort)val.Length);

            // Write the char's
            ResizeBuffer(val.Length);
            for (int i = 0; i < val.Length; i++)
            {
                Write((byte)val[i]);
            }
        }

        public void WriteChannel(ulong val)
        {
            WriteUint64(val);
        }

        public byte[] GetData()
        {
            return data;
        }

        public string GetMessage()
        {
            return Encoding.UTF8.GetString(data);
        }

        public string GetHexString()
        {
            return BitConverter.ToString(data).Replace("-", " ");
        }

        public int GetSize()
        {
            return size;
        }

        public int GetCapacity()
        {
            return capacity;
        }
    }
}
