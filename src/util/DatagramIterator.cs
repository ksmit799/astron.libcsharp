using System.Text;
using System.Diagnostics;
using System;

namespace astron.util
{
    public class DatagramIterator
    {
        protected byte[] data;
        protected int capacity;
        protected int index;

        public DatagramIterator()
        {
            data = new byte[0];
            capacity = 0;
            index = 0;
        }

        public DatagramIterator(byte[] dg, int offset = 0)
        {
            data = dg;
            capacity = dg.Length;
            index = offset;
        }

        public DatagramIterator(Datagram dg, int offset = 0)
        {
            data = dg.GetData();
            capacity = dg.GetCapacity();
            index = offset;
        }

        public byte Read()
        {
            Debug.Assert(index + 1 <= capacity);
            return data[index++];
        }

        public char ReadChar()
        {
            return (char)Read();
        }

        public sbyte ReadInt8()
        {
            return (sbyte)Read();
        }

        public byte ReadUint8()
        {
            return Read();
        }

        public short ReadInt16()
        {
            return (short)(Read() | Read() << 8);
        }

        public ushort ReadUint16()
        {
            return (ushort)(Read() | Read() << 8);
        }

        public int ReadInt32()
        {
            return (int)(Read() | Read() << 8 |
                         Read() << 16 | Read() << 24);
        }

        public uint ReadUint32()
        {
            return (uint)(Read() | Read() << 8 |
                          Read() << 16 | Read() << 24);
        }

        public long ReadInt64()
        {
            uint lo = (uint)(Read() | Read() << 8 |
                             Read() << 16 | Read() << 24);
            uint hi = (uint)(Read() | Read() << 8 |
                             Read() << 16 | Read() << 24);
            return (long)(ulong)hi << 32 | lo;
        }

        public ulong ReadUint64()
        {
            uint lo = (uint)(Read() | Read() << 8 |
                 Read() << 16 | Read() << 24);
            uint hi = (uint)(Read() | Read() << 8 |
                             Read() << 16 | Read() << 24);
            return (ulong)hi << 32 | lo;
        }

        public string ReadString()
        {
            // The length of the string.
            ushort length = ReadUint16();

            // Re-build the string.
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                builder.Append(ReadChar()); 
            }

            return builder.ToString();
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

        public string GetRemainingData()
        {
            byte[] remaining = new byte[GetRemainingSize()];
            Array.Copy(data, index, remaining, 0, GetRemainingSize());
            return Encoding.UTF8.GetString(remaining);
        }

        public int GetCurrentIndex()
        {
            return index;
        }

        public int GetRemainingSize()
        {
            return capacity - index;
        }
    }
}