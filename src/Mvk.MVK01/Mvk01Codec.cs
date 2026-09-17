using System;
using System.Text;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;

namespace Mvk.MVK01
{
    /// <summary>
    /// Декодирование значений МВК01 на основе <see cref="RegisterCodec"/>.
    /// </summary>
    public static class Mvk01Codec
    {
        /// <summary>Статус: операция успешно выполнена.</summary>
        public const uint StatusSuccess = 0x00000;

        /// <summary>Статус: клиент записал неверную команду.</summary>
        public const uint StatusBadCommand = 0x00BAD;

        /// <summary>Статус: ошибка флэш-памяти, конфигурация не сохранена.</summary>
        public const uint StatusFlashError = 0xF0BAD;

        /// <summary>
        /// Кодирует системную команду в два регистра.
        /// </summary>
        /// <param name="command">Системная команда.</param>
        /// <param name="byteOrder">Порядок передачи байт устройства.</param>
        public static ushort[] EncodeSystemCommand(Mvk01SystemCommand command, MvkByteOrder byteOrder)
        {
            return RegisterCodec.FromUInt32((uint)command, byteOrder);
        }

        /// <summary>
        /// Декодирует версию ПО (раздел 4.1.2).
        /// </summary>
        /// <param name="packed">Упакованное значение (например, 0x01071400 → 1.7.20.0).</param>
        public static Mvk01FirmwareVersion DecodeFirmwareVersion(uint packed)
        {
            return new Mvk01FirmwareVersion(
                (int)((packed >> 24) & 0xFF),
                (int)((packed >> 16) & 0xFF),
                (int)((packed >> 8) & 0xFF),
                (int)(packed & 0xFF));
        }

        /// <summary>
        /// Декодирует индикатор системных ошибок (раздел 4.1.3).
        /// </summary>
        /// <param name="raw">Значение регистра 0x1006.</param>
        public static Mvk01SystemErrors DecodeSystemErrors(uint raw)
        {
            return new Mvk01SystemErrors(
                DspReceiveError: (raw & 0b0000_0001) != 0,
                SdCardError: (raw & 0b0000_1110) != 0,
                DspSendError: (raw & 0b0001_0000) != 0,
                ModbusInitializationError: (raw & 0b0010_0000) != 0,
                ConfigurationCrcError: (raw & 0b0100_0000) != 0,
                Raw: raw);
        }

        /// <summary>
        /// Декодирует состояние контроля по агрегату (раздел 4.1.4).
        /// </summary>
        /// <param name="raw">Значение регистра состояния агрегата.</param>
        public static Mvk01UnitState DecodeUnitState(uint raw)
        {
            return new Mvk01UnitState(
                Zone: (Mvk01Zone)(raw & 0xFF),
                SensorFailureEvent: (raw & (1u << 9)) != 0,
                EnteredYellowZone: (raw & (1u << 10)) != 0,
                EnteredRedZone: (raw & (1u << 11)) != 0,
                Raw: raw);
        }

        /// <summary>
        /// Декодирует индикаторы состояния канала (раздел 5.1.4).
        /// </summary>
        /// <param name="raw">Значение регистра состояния канала.</param>
        public static Mvk01ChannelState DecodeChannelState(uint raw)
        {
            return new Mvk01ChannelState(
                Overload: (raw & (1u << 0)) != 0,
                Buffer16HzNotFilled: (raw & (1u << 4)) != 0,
                Buffer64HzNotFilled: (raw & (1u << 5)) != 0,
                Buffer512HzNotFilled: (raw & (1u << 6)) != 0,
                DcVoltageAboveMaximum: (raw & (1u << 10)) != 0,
                DcVoltageBelowMinimum: (raw & (1u << 11)) != 0,
                Raw: raw);
        }

        /// <summary>
        /// Декодирует индикаторы работы DSP (раздел 4.1.5).
        /// </summary>
        /// <param name="registers">Шесть регистров индикаторов (0x1020–0x1025).</param>
        /// <param name="byteOrder">Порядок передачи байт устройства.</param>
        public static Mvk01DspIndicators DecodeDspIndicators(
            ReadOnlySpan<ushort> registers,
            MvkByteOrder byteOrder)
        {
            if (registers.Length < 6)
            {
                throw new ArgumentException("Требуется шесть регистров индикаторов DSP.", nameof(registers));
            }

            uint overload = RegisterCodec.ToUInt32(registers[..2], byteOrder);
            uint configuration = RegisterCodec.ToUInt32(registers.Slice(2, 2), byteOrder);
            uint counter = RegisterCodec.ToUInt32(registers.Slice(4, 2), byteOrder);

            return new Mvk01DspIndicators((byte)overload, configuration, counter);
        }

        /// <summary>
        /// Декодирует метку времени тахометра (1/65536 с) в секунды.
        /// </summary>
        /// <param name="raw">Значение регистра метки времени.</param>
        public static double DecodeTachoTimestampSeconds(uint raw)
        {
            return raw / 65536.0;
        }

        /// <summary>
        /// Декодирует массив значений float из регистров.
        /// </summary>
        /// <param name="registers">Регистры (по два на значение).</param>
        /// <param name="valueCount">Число значений.</param>
        /// <param name="byteOrder">Порядок передачи байт устройства.</param>
        public static float[] DecodeFloats(
            ReadOnlySpan<ushort> registers,
            int valueCount,
            MvkByteOrder byteOrder)
        {
            if (registers.Length < valueCount * 2)
            {
                throw new ArgumentException(
                    $"Требуется {valueCount * 2} регистров для {valueCount} значений float.",
                    nameof(registers));
            }

            float[] values = new float[valueCount];
            for (int i = 0; i < valueCount; i++)
            {
                values[i] = RegisterCodec.ToSingle(registers.Slice(i * 2, 2), byteOrder);
            }

            return values;
        }

        /// <summary>
        /// Кодировка строк по умолчанию: кодовая страница 1251 (русские единицы измерения),
        /// при недоступности — Latin1.
        /// </summary>
        public static Encoding DefaultTextEncoding { get; } = CreateDefaultEncoding();

        /// <summary>
        /// Декодирует строку МВК01 из регистров (старший байт регистра — первый символ).
        /// Используется для серийного номера устройства.
        /// </summary>
        /// <param name="registers">Регистры со строкой.</param>
        /// <param name="byteCount">Число байт строки.</param>
        /// <param name="encoding">Кодировка; по умолчанию <see cref="DefaultTextEncoding"/>.</param>
        public static string DecodeText(
            ReadOnlySpan<ushort> registers,
            int byteCount,
            Encoding? encoding = null)
        {
            return RegisterCodec.ToText(registers, encoding ?? DefaultTextEncoding, byteCount);
        }

        /// <summary>
        /// Декодирует строку канала МВК01 (единицы измерения, серийный номер датчика):
        /// на устройстве байты внутри каждого 4-байтного слова хранятся в обратном порядке.
        /// </summary>
        /// <param name="registers">Регистры со строкой.</param>
        /// <param name="byteCount">Число байт строки.</param>
        /// <param name="encoding">Кодировка; по умолчанию <see cref="DefaultTextEncoding"/>.</param>
        public static string DecodeTextSwapped(
            ReadOnlySpan<ushort> registers,
            int byteCount,
            Encoding? encoding = null)
        {
            byte[] bytes = RegisterCodec.ToBytes(registers, byteCount);
            SwapWords(bytes);

            int zeroIndex = Array.IndexOf(bytes, (byte)0);
            ReadOnlySpan<byte> content = zeroIndex >= 0 ? bytes.AsSpan(0, zeroIndex) : bytes;

            return (encoding ?? DefaultTextEncoding).GetString(content).Trim();
        }

        /// <summary>
        /// Кодирует строку канала МВК01 в регистры (обратное к <see cref="DecodeTextSwapped"/>).
        /// </summary>
        /// <param name="text">Строка.</param>
        /// <param name="registerCount">Число регистров для строки.</param>
        /// <param name="encoding">Кодировка; по умолчанию <see cref="DefaultTextEncoding"/>.</param>
        public static ushort[] EncodeTextSwapped(string text, int registerCount, Encoding? encoding = null)
        {
            ArgumentNullException.ThrowIfNull(text);

            if (registerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(registerCount), registerCount, "Число регистров должно быть больше нуля.");
            }

            byte[] bytes = new byte[registerCount * 2];
            byte[] raw = (encoding ?? DefaultTextEncoding).GetBytes(text);
            if (raw.Length > bytes.Length)
            {
                throw new ArgumentException(
                    $"Строка занимает {raw.Length} байт и не помещается в {registerCount} регистров.",
                    nameof(text));
            }

            raw.CopyTo(bytes, 0);
            SwapWords(bytes);

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
        /// Преобразует байты в строку MD5 (нижний регистр, без разделителей).
        /// </summary>
        /// <param name="md5Bytes">16 байт хэша.</param>
        public static string FormatMd5(ReadOnlySpan<byte> md5Bytes)
        {
            return Convert.ToHexString(md5Bytes).ToLowerInvariant();
        }

        private static void SwapWords(byte[] bytes)
        {
            for (int offset = 0; offset + 4 <= bytes.Length; offset += 4)
            {
                (bytes[offset], bytes[offset + 3]) = (bytes[offset + 3], bytes[offset]);
                (bytes[offset + 1], bytes[offset + 2]) = (bytes[offset + 2], bytes[offset + 1]);
            }
        }

        private static Encoding CreateDefaultEncoding()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1251);
            }
            catch (Exception)
            {
                return Encoding.Latin1;
            }
        }
    }
}
