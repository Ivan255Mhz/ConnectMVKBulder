using System;
using System.Collections.Generic;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Проверка набора уставок.
    /// </summary>
    public static class MvkSetpointValidator
    {
        /// <summary>
        /// Проверяет уставки и возвращает список ошибок. Пустой список — ошибок нет.
        /// </summary>
        /// <param name="setpoints">Проверяемые уставки.</param>
        public static IReadOnlyList<string> Validate(IEnumerable<MvkSetpoint> setpoints)
        {
            ArgumentNullException.ThrowIfNull(setpoints);

            List<string> failures = [];
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);

            foreach (MvkSetpoint setpoint in setpoints)
            {
                if (setpoint is null)
                {
                    failures.Add("Набор уставок содержит null.");
                    continue;
                }

                string id = setpoint.Id?.Trim() ?? string.Empty;
                if (id.Length == 0)
                {
                    failures.Add("У уставки не задан обязательный идентификатор Id.");
                }
                else if (!ids.Add(id))
                {
                    failures.Add($"Уставка '{id}': идентификатор Id не уникален.");
                }

                if (string.IsNullOrWhiteSpace(setpoint.DeviceId))
                {
                    failures.Add($"Уставка '{id}': не задан DeviceId.");
                }

                int measurement = (int)setpoint.Measurement;
                if (measurement < 1 || measurement > Mvk01RegisterMap.MaxMeasurementNumber)
                {
                    failures.Add(
                        $"Уставка '{id}': номер параметра {measurement} вне диапазона "
                        + $"1..{Mvk01RegisterMap.MaxMeasurementNumber}.");
                }

                if (setpoint.Channel < 1 || setpoint.Channel > Mvk01RegisterMap.MaxChannels)
                {
                    failures.Add(
                        $"Уставка '{id}': канал {setpoint.Channel} вне диапазона "
                        + $"1..{Mvk01RegisterMap.MaxChannels}.");
                }

                if (!Enum.IsDefined(setpoint.Comparison))
                {
                    failures.Add($"Уставка '{id}': неизвестное правило сравнения {setpoint.Comparison}.");
                }

                if (!double.IsFinite(setpoint.Threshold))
                {
                    failures.Add($"Уставка '{id}': порог должен быть конечным числом.");
                }

                if (setpoint.UpperThreshold is double upperValue && !double.IsFinite(upperValue))
                {
                    failures.Add($"Уставка '{id}': верхняя граница должна быть конечным числом.");
                }

                if (setpoint.Comparison is MvkSetpointComparison.Outside or MvkSetpointComparison.Inside)
                {
                    if (setpoint.UpperThreshold is not double rangeUpper)
                    {
                        failures.Add($"Уставка '{id}': для {setpoint.Comparison} нужна UpperThreshold.");
                    }
                    else if (rangeUpper <= setpoint.Threshold)
                    {
                        failures.Add($"Уставка '{id}': UpperThreshold должна быть больше Threshold.");
                    }
                    else if (setpoint.Comparison == MvkSetpointComparison.Outside
                        && setpoint.Hysteresis * 2 >= rangeUpper - setpoint.Threshold)
                    {
                        failures.Add(
                            $"Уставка '{id}': гистерезис слишком велик для диапазона — "
                            + "тревога никогда не снимется.");
                    }
                }

                if (!double.IsFinite(setpoint.Hysteresis) || setpoint.Hysteresis < 0)
                {
                    failures.Add($"Уставка '{id}': гистерезис должен быть конечным неотрицательным числом.");
                }

                if (setpoint.ActivationDelay < TimeSpan.Zero)
                {
                    failures.Add($"Уставка '{id}': задержка срабатывания не может быть отрицательной.");
                }

                if (!Enum.IsDefined(setpoint.Severity))
                {
                    failures.Add($"Уставка '{id}': неизвестная важность {setpoint.Severity}.");
                }
            }

            return failures;
        }

        /// <summary>
        /// Проверяет уставки и выбрасывает <see cref="MvkSetpointValidationException"/> при ошибках.
        /// </summary>
        /// <param name="setpoints">Проверяемые уставки.</param>
        public static void ThrowIfInvalid(IEnumerable<MvkSetpoint> setpoints)
        {
            IReadOnlyList<string> failures = Validate(setpoints);
            if (failures.Count > 0)
            {
                throw new MvkSetpointValidationException(failures);
            }
        }
    }
}
