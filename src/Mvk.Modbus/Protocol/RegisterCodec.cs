using System;
using System.Buffers.Binary;
using System.Text;
using Mvk.Modbus.Configuration;

namespace Mvk.Modbus.Protocol
{
    /// <summary>
    /// Преобразование 16-битных регистров Modbus в типизированные значения и обратно
    /// с учётом порядка передачи байт устройства (<see cref="MvkByteOrder"/>,
    /// регистр 0x000C МВК01).
    /// </summary>
    public static class RegisterCodec
    {
        /// <summary>
        /// Декодирует 32-битное беззнаковое значение из двух регистров.
        /// </summary>
        /// <param name="registers">Регистры (минимум два).</param>
        /// <param name="byteOrder">Порядок передачи байт устройства.</param>
        public static uint ToUInt32(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)
        {
            if (registers.Length < 2)
            {
                throw new ArgumentException("Для 32-битного значения требуется минимум два регистра.", nameof(registers));
            }

            uint ordered = ((uint)registers[0] << 16) | registers[1];

            return byteOrder switch
            {
                MvkByteOrder.BigEndian => ordered,
                MvkByteOrder.LittleEndian => BinaryPrimitives.ReverseEndianness(ordered),
                MvkByteOrder.Mixed => ((uint)registers[1] << 16) | registers[0],
                _ => throw new ArgumentOutOfRangeException(nameof(byteOrder), byteOrder, "Неизвестный порядок байт.")
            };
        }

        /// <summary>
        /// Декодирует 32-битное знаковое значение из двух регистров.
        /// </summary>
        /// <param name="registers">Регистры (минимум два).</param>
        /// <param name="byteOrder">Порядок передачи байт устройства.</param>
        public static int ToInt32(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)
        {
            return unchecked((int)ToUInt32(registers, byteOrder));
        }

        /// <summary>
        /// Декодирует число с плавающей точкой (single) из двух регистров.
        /// </summary>
        /// <param name="registers">Регистры (минимум два).</param>
        /// <param name="byteOrder">Порядок передачи байт устройства.</param>
        public static float ToSingle(ReadOnlySpan<ushort> registers, MvkByteOrder byteOrder)
        {
            return BitConverter.UInt32BitsToSingle(ToUInt32(registers, byteOrder));
        }

        /// <summary>
        /// Кодирует 32-битное беззнаковое значение в два регистра.
        /// </summary>
        /// <param name="value">Значение.</param>
        /// <param name="byteOrder">Порядок передачи байт устройства.</param>
        public static ushort[] FromUInt32(uint value, MvkByteOrder byteOrder)
        {
            uint ordered = byteOrder switch
            {
                MvkByteOrder.BigEndian => value,
                MvkByteOrder.LittleEndian => BinaryPrimitives.ReverseEndianness(value),
                MvkByteOrder.Mixed => value,
                _ => throw new ArgumentOutOfRangeException(nameof(byteOrder), byteOrder, "Неизвестный порядок байт.")
            };

            ushort first = (ushort)(ordered >> 16);
            ushort second = (ushort)(ordered & 0xFFFF);

            return byteOrder == MvkByteOrder.Mixed
                ? [second, first]
                : [first, second];
        }

        /// <summary>
        /// Кодирует число с плавающей точкой (single) в два регистра.
        /// </summary>
        /// <param name="value">Значение.</param>
        /// <param name="byteOrder">Порядок передачи байт устройства.</param>
        public static ushort[] FromSingle(float value, MvkByteOrder byteOrder)
        {
            return FromUInt32(BitConverter.SingleToUInt32Bits(value), byteOrder);
        }

        /// <summary>
        /// Преобразует регистры в байты: старший байт регистра — первый.
        /// </summary>
        /// <param name="registers">Регистры.</param>
        /// <param name="byteCount">Число байт; -1 — все байты регистров.</param>
        public static byte[] ToBytes(ReadOnlySpan<ushort> registers, int byteCount = -1)
        {
            int totalBytes = registers.Length * 2;
            if (byteCount >= 0)
            {
                totalBytes = Math.Min(byteCount, totalBytes);
            }

            byte[] bytes = new byte[totalBytes];
            for (int i = 0; i < totalBytes; i++)
            {
                ushort register = registers[i / 2];
                bytes[i] = i % 2 == 0
                    ? (byte)(register >> 8)
                    : (byte)register;
            }

            return bytes;
        }

        /// <summary>
        /// Декодирует строку из регистров: старший байт регистра — первый символ.
        /// Строка обрезается по первому нулевому байту, пробелы по краям удаляются.
        /// </summary>
        /// <param name="registers">Регистры со строкой.</param>
        /// <param name="encoding">Кодировка строки устройства (например, кодовая страница 1251).</param>
        /// <param name="byteCount">Число байт строки; -1 — весь массив регистров.</param>
        public static string ToText(ReadOnlySpan<ushort> registers, Encoding encoding, int byteCount = -1)
        {
            ArgumentNullException.ThrowIfNull(encoding);

            int totalBytes = registers.Length * 2;
            if (byteCount >= 0)
            {
                totalBytes = Math.Min(byteCount, totalBytes);
            }

            byte[] bytes = new byte[totalBytes];
            for (int i = 0; i < totalBytes; i++)
            {
                ushort register = registers[i / 2];
                bytes[i] = i % 2 == 0
                    ? (byte)(register >> 8)
                    : (byte)register;
            }

            int zeroIndex = Array.IndexOf(bytes, (byte)0);
            ReadOnlySpan<byte> content = zeroIndex >= 0 ? bytes.AsSpan(0, zeroIndex) : bytes;

            return encoding.GetString(content).Trim();
        }

        /// <summary>
        /// Кодирует строку в регистры: старший байт регистра — первый символ,
        /// свободные байты заполняются нулями.
        /// </summary>
        /// <param name="text">Строка.</param>
        /// <param name="registerCount">Число регистров для строки.</param>
        /// <param name="encoding">Кодировка строки устройства.</param>
        public static ushort[] FromText(string text, int registerCount, Encoding encoding)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(encoding);

            if (registerCount is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(registerCount), registerCount, "Число регистров должно быть больше нуля.");
            }

            int capacity = registerCount * 2;
            byte[] bytes = encoding.GetBytes(text);
            if (bytes.Length > capacity)
            {
                throw new ArgumentException($"Строка занимает {bytes.Length} байт и не помещается в {registerCount} регистров ({capacity} байт).", nameof(text));
            }

            ushort[] registers = new ushort[registerCount];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i % 2 == 0)
                {
                    registers[i / 2] = (ushort)(bytes[i] << 8);
                }
                else
                {
                    registers[i / 2] |= bytes[i];
                }
            }

            return registers;
        }

        /// <summary>
        /// Проверяет бит 16-битного значения.
        /// </summary>
        /// <param name="value">Значение.</param>
        /// <param name="bitIndex">Номер бита (0–15).</param>
        public static bool GetBit(ushort value, int bitIndex)
        {
            ValidateBitIndex(bitIndex, 15);
            return (value & (1 << bitIndex)) != 0;
        }

        /// <summary>
        /// Проверяет бит 32-битного значения.
        /// </summary>
        /// <param name="value">Значение.</param>
        /// <param name="bitIndex">Номер бита (0–31).</param>
        public static bool GetBit(uint value, int bitIndex)
        {
            ValidateBitIndex(bitIndex, 31);
            return (value & (1u << bitIndex)) != 0;
        }

        private static void ValidateBitIndex(int bitIndex, int maxIndex)
        {
            if (bitIndex < 0 || bitIndex > maxIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(bitIndex), bitIndex, $"Номер бита должен быть в диапазоне 0..{maxIndex}.");
            }
        }
    }
}
