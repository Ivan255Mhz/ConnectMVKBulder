using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Mvk.Modbus.Configuration
{
    /// <summary>
    /// Проверяет корректность секции <see cref="MvkModbusOptions"/>: диапазоны значений,
    /// обязательные поля и уникальность идентификаторов устройств.
    /// </summary>
    public sealed class MvkModbusOptionsValidator : IValidateOptions<MvkModbusOptions>
    {
        /// <inheritdoc/>
        public ValidateOptionsResult Validate(string? name, MvkModbusOptions options)
        {
            if (options is null)
            {
                return ValidateOptionsResult.Fail("Секция конфигурации MvkModbus не задана.");
            }

            List<string> failures = [];
            MvkDefaultsOptions defaults = options.Defaults ?? new MvkDefaultsOptions();

            ValidateDefaults(defaults, failures);

            if (options.Devices is not null)
            {
                HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);

                foreach (MvkDeviceOptions device in options.Devices)
                {
                    if (device is null)
                    {
                        failures.Add("В списке устройств найден пустой элемент.");
                        continue;
                    }

                    string id = device.Id?.Trim() ?? string.Empty;
                    if (id.Length == 0)
                    {
                        failures.Add("У устройства не задан обязательный идентификатор Id.");
                        continue;
                    }

                    if (!ids.Add(id))
                    {
                        failures.Add($"Устройство '{id}': идентификатор Id не уникален.");
                    }

                    if (string.IsNullOrWhiteSpace(device.Host))
                    {
                        failures.Add($"Устройство '{id}': не задан адрес Host.");
                    }

                    ValidateDevice(device.Resolve(defaults), failures);
                }
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        private static void ValidateDefaults(MvkDefaultsOptions defaults, List<string> failures)
        {
            AddIfOutOfRange(failures, "Defaults.Port", defaults.Port, 1, 65535);
            AddIfOutOfRange(failures, "Defaults.SlaveId", defaults.SlaveId, 1, 247);
            AddIfOutOfRange(failures, "Defaults.ConnectTimeoutMs", defaults.ConnectTimeoutMs, 1, 600_000);
            AddIfOutOfRange(failures, "Defaults.ReadTimeoutMs", defaults.ReadTimeoutMs, 1, 600_000);
            AddIfOutOfRange(failures, "Defaults.WriteTimeoutMs", defaults.WriteTimeoutMs, 1, 600_000);
            AddIfOutOfRange(failures, "Defaults.MaxRetries", defaults.MaxRetries, 0, 10);
            AddIfOutOfRange(failures, "Defaults.RetryDelayMs", defaults.RetryDelayMs, 0, 60_000);
            AddIfOutOfRange(failures, "Defaults.ReconnectBaseDelayMs", defaults.ReconnectBaseDelayMs, 1, 600_000);
            AddIfOutOfRange(failures, "Defaults.ReconnectMaxDelayMs", defaults.ReconnectMaxDelayMs, 1, 3_600_000);
            AddIfOutOfRange(failures, "Defaults.IdleDisconnectSeconds", defaults.IdleDisconnectSeconds, 0, 86_400);
            AddIfOutOfRange(failures, "Defaults.MaxConcurrentRequests", defaults.MaxConcurrentRequests, 1, 64);

            if (defaults.ReconnectMaxDelayMs < defaults.ReconnectBaseDelayMs)
            {
                failures.Add("Defaults.ReconnectMaxDelayMs не может быть меньше Defaults.ReconnectBaseDelayMs.");
            }

            if (defaults.ProtectedWriteEndAddress < defaults.ProtectedWriteStartAddress)
            {
                failures.Add("Defaults.ProtectedWriteEndAddress не может быть меньше Defaults.ProtectedWriteStartAddress.");
            }
        }

        private static void ValidateDevice(MvkDeviceSettings device, List<string> failures)
        {
            AddIfOutOfRange(failures, $"Устройство '{device.Id}'.Port", device.Port, 1, 65535);
            AddIfOutOfRange(failures, $"Устройство '{device.Id}'.SlaveId", device.SlaveId, 1, 247);
            AddIfOutOfRange(failures, $"Устройство '{device.Id}'.MaxConcurrentRequests", device.MaxConcurrentRequests, 1, 64);
            AddIfOutOfRange(failures, $"Устройство '{device.Id}'.ConnectTimeoutMs", device.ConnectTimeoutMs, 1, 600_000);
            AddIfOutOfRange(failures, $"Устройство '{device.Id}'.ReadTimeoutMs", device.ReadTimeoutMs, 1, 600_000);
            AddIfOutOfRange(failures, $"Устройство '{device.Id}'.WriteTimeoutMs", device.WriteTimeoutMs, 1, 600_000);
            AddIfOutOfRange(failures, $"Устройство '{device.Id}'.MaxRetries", device.MaxRetries, 0, 10);
            AddIfOutOfRange(failures, $"Устройство '{device.Id}'.IdleDisconnectSeconds", device.IdleDisconnectSeconds, 0, 86_400);
        }

        private static void AddIfOutOfRange(List<string> failures, string path, long value, long min, long max)
        {
            if (value < min || value > max)
            {
                failures.Add($"{path} = {value} вне допустимого диапазона {min}..{max}.");
            }
        }
    }
}
