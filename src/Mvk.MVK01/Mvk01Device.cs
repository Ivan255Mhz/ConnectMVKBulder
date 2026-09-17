using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;

namespace Mvk.MVK01
{
    /// <summary>
    /// Типизированный фасад МВК01 поверх <see cref="IMvkDevice"/>: декодирует
    /// значения регистров и выполняет системные команды с ожиданием статуса.
    /// </summary>
    public sealed class Mvk01Device : IMvk01Device
    {
        private const int CommandPollIntervalMs = 200;
        private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(3);

        private static readonly string[] RmsSetNames =
        [
            "10…3000 Гц",
            "10…2000 Гц",
            "10…1000 Гц",
            "2…1000 Гц",
            "0.8…300 Гц",
            "0.8…150 Гц",
            "Фильтр 1",
            "Фильтр 2"
        ];

        private readonly IMvkDevice device;
        private readonly Encoding textEncoding;

        /// <summary>
        /// Создаёт фасад устройства.
        /// </summary>
        /// <param name="device">Базовое устройство пула.</param>
        /// <param name="textEncoding">
        /// Кодировка строк устройства (единицы измерения, серийные номера).
        /// По умолчанию <see cref="Mvk01Codec.DefaultTextEncoding"/> — кодовая страница 1251.
        /// </param>
        public Mvk01Device(IMvkDevice device, Encoding? textEncoding = null)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.textEncoding = textEncoding ?? Mvk01Codec.DefaultTextEncoding;
        }

        /// <inheritdoc/>
        public string Id => device.Settings.Id;

        /// <inheritdoc/>
        public MvkDeviceSettings Settings => device.Settings;

        /// <inheritdoc/>
        public MvkDeviceState State => device.State;

        /// <inheritdoc/>
        public IMvkDevice Device => device;

        /// <inheritdoc/>
        public Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
        {
            return device.EnsureConnectedAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return device.DisconnectAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<Mvk01DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            ushort[] main = await ReadRegistersAsync(Mvk01RegisterMap.DeviceSerialNumber, 12, cancellationToken)
                .ConfigureAwait(false);
            ushort[] md5 = await ReadRegistersAsync(Mvk01RegisterMap.DspMd5, 12, cancellationToken)
                .ConfigureAwait(false);
            ushort[] dsp = await ReadRegistersAsync(Mvk01RegisterMap.DspIndicators, 6, cancellationToken)
                .ConfigureAwait(false);

            MvkByteOrder order = Settings.ByteOrder;

            return new Mvk01DeviceInfo
            {
                SerialNumber = Mvk01Codec.DecodeText(main.AsSpan(0, 4), 8, textEncoding),
                FirmwareVersion = Mvk01Codec.DecodeFirmwareVersion(RegisterCodec.ToUInt32(main.AsSpan(4, 2), order)),
                Errors = Mvk01Codec.DecodeSystemErrors(RegisterCodec.ToUInt32(main.AsSpan(6, 2), order)),
                Uptime = TimeSpan.FromSeconds(RegisterCodec.ToUInt32(main.AsSpan(8, 2), order)),
                MemoryCardKilobytes = (int)RegisterCodec.ToUInt32(main.AsSpan(10, 2), order),
                DspMd5 = Mvk01Codec.FormatMd5(RegisterCodec.ToBytes(md5.AsSpan(0, 8), 16)),
                Unit1State = Mvk01Codec.DecodeUnitState(RegisterCodec.ToUInt32(md5.AsSpan(8, 2), order)),
                Unit2State = Mvk01Codec.DecodeUnitState(RegisterCodec.ToUInt32(md5.AsSpan(10, 2), order)),
                DspIndicators = Mvk01Codec.DecodeDspIndicators(dsp, order)
            };
        }

        /// <inheritdoc/>
        public async Task<Mvk01Channel> ReadChannelAsync(int channel, CancellationToken cancellationToken = default)
        {
            ValidateChannel(channel);

            ushort baseAddress = Mvk01RegisterMap.ChannelData(channel);
            MvkByteOrder order = Settings.ByteOrder;

            ushort[] primary = await ReadRegistersAsync(baseAddress, 0x60, cancellationToken).ConfigureAwait(false);
            ushort[] rmsRegisters = await ReadRegistersAsync(
                (ushort)(baseAddress + Mvk01RegisterMap.RmsSetOffset),
                Mvk01RegisterMap.RmsSetRegisterCount,
                cancellationToken).ConfigureAwait(false);
            ushort[] extra = await ReadRegistersAsync(
                (ushort)(baseAddress + Mvk01RegisterMap.DcComponentOffset),
                6,
                cancellationToken).ConfigureAwait(false);
            ushort[] peaks = await ReadRegistersAsync(
                (ushort)(baseAddress + Mvk01RegisterMap.EquivalentPeakOffset),
                Mvk01RegisterMap.EquivalentRegisterCount,
                cancellationToken).ConfigureAwait(false);
            ushort[] ranges = await ReadRegistersAsync(
                (ushort)(baseAddress + Mvk01RegisterMap.EquivalentRangeOffset),
                Mvk01RegisterMap.EquivalentRegisterCount,
                cancellationToken).ConfigureAwait(false);

            float[] rmsValues = Mvk01Codec.DecodeFloats(rmsRegisters, Mvk01RegisterMap.RmsSetCount * Mvk01RegisterMap.RmsSetValues, order);
            List<Mvk01RmsSet> rmsSets = new(Mvk01RegisterMap.RmsSetCount);
            for (int i = 0; i < Mvk01RegisterMap.RmsSetCount; i++)
            {
                rmsSets.Add(new Mvk01RmsSet(
                    RmsSetNames[i],
                    rmsValues[i * Mvk01RegisterMap.RmsSetValues],
                    rmsValues[i * Mvk01RegisterMap.RmsSetValues + 1],
                    rmsValues[i * Mvk01RegisterMap.RmsSetValues + 2]));
            }

            return new Mvk01Channel
            {
                Channel = channel,
                SensorVoltage = RegisterCodec.ToSingle(primary.AsSpan(0, 2), order),
                Spectrum = Mvk01Codec.DecodeFloats(
                    primary.AsSpan(Mvk01RegisterMap.SpectrumOffset, Mvk01RegisterMap.SpectrumRegisterCount),
                    Mvk01RegisterMap.SpectrumBands,
                    order),
                PeakFactor = RegisterCodec.ToSingle(primary.AsSpan(Mvk01RegisterMap.PeakFactorOffset, 2), order),
                RmsX25kHzAcceleration = RegisterCodec.ToSingle(primary.AsSpan(Mvk01RegisterMap.RmsX25kHzOffset, 2), order),
                RmsSets = rmsSets,
                DcComponent = RegisterCodec.ToSingle(extra.AsSpan(0, 2), order),
                State = Mvk01Codec.DecodeChannelState(RegisterCodec.ToUInt32(extra.AsSpan(4, 2), order)),
                EquivalentPeakValues = Mvk01Codec.DecodeFloats(peaks, Mvk01RegisterMap.EquivalentValuesCount, order),
                EquivalentRangeValues = Mvk01Codec.DecodeFloats(ranges, Mvk01RegisterMap.EquivalentValuesCount, order)
            };
        }

        /// <inheritdoc/>
        public async Task<Mvk01ChannelConfig> ReadChannelConfigAsync(int channel, CancellationToken cancellationToken = default)
        {
            ValidateChannel(channel);

            ushort baseAddress = Mvk01RegisterMap.ChannelConfig(channel);
            MvkByteOrder order = Settings.ByteOrder;

            ushort[] head = await ReadRegistersAsync(baseAddress, 6, cancellationToken).ConfigureAwait(false);
            ushort[] units = await ReadRegistersAsync(
                (ushort)(baseAddress + Mvk01RegisterMap.ChannelUnitsOffset), 16, cancellationToken).ConfigureAwait(false);
            ushort[] serial = await ReadRegistersAsync(
                (ushort)(baseAddress + Mvk01RegisterMap.ChannelSerialOffset), 16, cancellationToken).ConfigureAwait(false);
            ushort[] filters = await ReadRegistersAsync(
                (ushort)(baseAddress + Mvk01RegisterMap.ChannelFilterX25kHzOffset), 10, cancellationToken).ConfigureAwait(false);
            ushort[] voltages = await ReadRegistersAsync(
                (ushort)(baseAddress + Mvk01RegisterMap.ChannelMaxDcVoltageOffset), 4, cancellationToken).ConfigureAwait(false);

            return new Mvk01ChannelConfig
            {
                Channel = channel,
                ChannelType = (int)RegisterCodec.ToUInt32(head.AsSpan(0, 2), order),
                SensorFactor = RegisterCodec.ToSingle(head.AsSpan(2, 2), order),
                OverloadThreshold = RegisterCodec.ToSingle(head.AsSpan(4, 2), order),
                Units = Mvk01Codec.DecodeTextSwapped(units, 32, textEncoding),
                SerialNumber = Mvk01Codec.DecodeTextSwapped(serial, 32, textEncoding),
                FilterX25kHz = (Mvk01FilterX25kHz)RegisterCodec.ToUInt32(filters.AsSpan(0, 2), order),
                Filter1LowBand = (int)RegisterCodec.ToUInt32(filters.AsSpan(2, 2), order),
                Filter1HighBand = (int)RegisterCodec.ToUInt32(filters.AsSpan(4, 2), order),
                Filter2LowBand = (int)RegisterCodec.ToUInt32(filters.AsSpan(6, 2), order),
                Filter2HighBand = (int)RegisterCodec.ToUInt32(filters.AsSpan(8, 2), order),
                MaxDcVoltage = RegisterCodec.ToSingle(voltages.AsSpan(0, 2), order),
                MinDcVoltage = RegisterCodec.ToSingle(voltages.AsSpan(2, 2), order)
            };
        }

        /// <inheritdoc/>
        public async Task<Mvk01TachoMeasurement> ReadTachoAsync(int tacho, CancellationToken cancellationToken = default)
        {
            if (tacho < 1 || tacho > Mvk01RegisterMap.MaxTachoChannels)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tacho),
                    tacho,
                    $"Номер тахометра должен быть в диапазоне 1..{Mvk01RegisterMap.MaxTachoChannels}.");
            }

            ushort baseAddress = Mvk01RegisterMap.TachoData(tacho);
            MvkByteOrder order = Settings.ByteOrder;

            ushort[] data = await ReadRegistersAsync(
                baseAddress, Mvk01RegisterMap.TachoDataRegisterCount, cancellationToken).ConfigureAwait(false);

            return new Mvk01TachoMeasurement
            {
                Tacho = tacho,
                FrequencyHz = RegisterCodec.ToSingle(data.AsSpan(Mvk01RegisterMap.TachoFrequencyOffset, 2), order),
                AverageIncrementHz = RegisterCodec.ToSingle(data.AsSpan(Mvk01RegisterMap.TachoAverageIncrementOffset, 2), order),
                SensorError = RegisterCodec.ToUInt32(data.AsSpan(Mvk01RegisterMap.TachoSensorErrorOffset, 2), order) != 0,
                TimestampSeconds = Mvk01Codec.DecodeTachoTimestampSeconds(
                    RegisterCodec.ToUInt32(data.AsSpan(Mvk01RegisterMap.TachoTimestampOffset, 2), order)),
                InputLevel = RegisterCodec.ToUInt32(data.AsSpan(Mvk01RegisterMap.TachoInputLevelOffset, 2), order) != 0,
                Direction = (Mvk01RotationDirection)RegisterCodec.ToUInt32(
                    data.AsSpan(Mvk01RegisterMap.TachoDirectionOffset, 2), order),
                TransitionCount = RegisterCodec.ToUInt32(data.AsSpan(Mvk01RegisterMap.TachoTransitionsOffset, 2), order)
            };
        }

        /// <inheritdoc/>
        public Task<float[]> ReadMeasurementByNumberAsync(
            Mvk01MeasurementNumber number,
            CancellationToken cancellationToken = default)
        {
            return ReadMeasurementByNumberAsync((int)number, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<float[]> ReadMeasurementByNumberAsync(int number, CancellationToken cancellationToken = default)
        {
            ValidateMeasurementNumber(number);

            ushort[] registers = await ReadMeasurementRawByNumberAsync(number, cancellationToken).ConfigureAwait(false);

            return Mvk01Codec.DecodeFloats(
                registers,
                Mvk01RegisterMap.MeasurementByNumberChannels,
                Settings.ByteOrder);
        }

        /// <inheritdoc/>
        public Task<ushort[]> ReadMeasurementRawByNumberAsync(int number, CancellationToken cancellationToken = default)
        {
            ValidateMeasurementNumber(number);

            return ReadRegistersAsync(
                Mvk01RegisterMap.MeasurementByNumber(number),
                Mvk01RegisterMap.MeasurementByNumberRegisterCount,
                cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<Mvk01CommandResult> ExecuteSystemCommandAsync(
            Mvk01SystemCommand command,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            TimeSpan effectiveTimeout = timeout ?? DefaultCommandTimeout;
            ushort[] commandRegisters = Mvk01Codec.EncodeSystemCommand(command, Settings.ByteOrder);

            ModbusResponse writeResponse = await device.ExecuteAsync(
                ModbusRequest.WriteMultipleRegisters(Id, Mvk01RegisterMap.SystemCommand, commandRegisters),
                effectiveTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!writeResponse.IsSuccess)
            {
                return new Mvk01CommandResult
                {
                    Command = command,
                    Status = Mvk01CommandStatus.TransportError,
                    Error = writeResponse.Error
                };
            }

            DateTimeOffset deadline = DateTimeOffset.UtcNow + effectiveTimeout;
            TimeSpan pollTimeout = effectiveTimeout < TimeSpan.FromSeconds(1)
                ? effectiveTimeout
                : TimeSpan.FromSeconds(1);

            while (DateTimeOffset.UtcNow < deadline)
            {
                ModbusResponse readResponse = await device.ExecuteAsync(
                    ModbusRequest.ReadHoldingRegisters(Id, Mvk01RegisterMap.SystemCommand, 2),
                    pollTimeout,
                    cancellationToken).ConfigureAwait(false);

                if (readResponse.IsSuccess)
                {
                    uint status = RegisterCodec.ToUInt32(readResponse.Data, Settings.ByteOrder);

                    switch (status)
                    {
                        case Mvk01Codec.StatusSuccess:
                            return new Mvk01CommandResult
                            {
                                Command = command,
                                Status = Mvk01CommandStatus.Success,
                                DeviceStatus = status
                            };

                        case Mvk01Codec.StatusBadCommand:
                            return new Mvk01CommandResult
                            {
                                Command = command,
                                Status = Mvk01CommandStatus.BadCommand,
                                DeviceStatus = status
                            };

                        case Mvk01Codec.StatusFlashError:
                            return new Mvk01CommandResult
                            {
                                Command = command,
                                Status = Mvk01CommandStatus.FlashError,
                                DeviceStatus = status
                            };
                    }
                }
                else if (readResponse.Error?.IsTransient != true)
                {
                    return new Mvk01CommandResult
                    {
                        Command = command,
                        Status = Mvk01CommandStatus.TransportError,
                        Error = readResponse.Error
                    };
                }

                await Task.Delay(CommandPollIntervalMs, cancellationToken).ConfigureAwait(false);
            }

            return new Mvk01CommandResult
            {
                Command = command,
                Status = Mvk01CommandStatus.Timeout
            };
        }

        /// <inheritdoc/>
        public Task<Mvk01CommandResult> SaveConfigurationAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSystemCommandAsync(Mvk01SystemCommand.SaveConfiguration, cancellationToken: cancellationToken);
        }

        /// <inheritdoc/>
        public Task<Mvk01CommandResult> RestoreConfigurationAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSystemCommandAsync(Mvk01SystemCommand.RestoreConfiguration, cancellationToken: cancellationToken);
        }

        /// <inheritdoc/>
        public Task<Mvk01CommandResult> RebootAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSystemCommandAsync(Mvk01SystemCommand.Reboot, cancellationToken: cancellationToken);
        }

        private Task<ushort[]> ReadRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken)
        {
            return device.ReadHoldingRegistersAsync(address, quantity, cancellationToken);
        }

        private static void ValidateChannel(int channel)
        {
            if (channel < 1 || channel > Mvk01RegisterMap.MaxChannels)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channel),
                    channel,
                    $"Номер канала должен быть в диапазоне 1..{Mvk01RegisterMap.MaxChannels}.");
            }
        }

        private static void ValidateMeasurementNumber(int number)
        {
            if (number < 1 || number > Mvk01RegisterMap.MaxMeasurementNumber)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(number),
                    number,
                    $"Номер параметра должен быть в диапазоне 1..{Mvk01RegisterMap.MaxMeasurementNumber}.");
            }
        }
    }
}
