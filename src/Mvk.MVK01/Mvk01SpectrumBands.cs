using System;

namespace Mvk.MVK01
{
    /// <summary>
    /// Полосы дольоктавного спектра МВК01: 43 полосы, средние частоты в герцах
    /// (раздел 5.1.3 карты регистров).
    /// </summary>
    public static class Mvk01SpectrumBands
    {
        private static readonly double[] Frequencies =
        [
            0.63, 0.8, 1.0, 1.25, 1.6, 2.0, 2.5, 3.15,
            4.0, 5.0, 6.3, 8.0, 10.0, 12.5, 16.0,
            20.0, 25.0, 31.5, 40.0, 50.0, 63.0, 80.0, 100.0,
            125.0, 160.0, 200.0, 250.0, 315.0, 400.0, 500.0,
            630.0, 800.0, 1000.0, 1250.0, 1600.0, 2000.0, 2500.0, 3150.0,
            4000.0, 5000.0, 6300.0, 8000.0, 10000.0
        ];

        /// <summary>
        /// Число полос спектра.
        /// </summary>
        public static int Count => Frequencies.Length;

        /// <summary>
        /// Средние частоты полос (Гц).
        /// </summary>
        public static ReadOnlySpan<double> CenterFrequencies => Frequencies;

        /// <summary>
        /// Возвращает среднюю частоту полосы.
        /// </summary>
        /// <param name="bandNumber">Номер полосы (1–43).</param>
        public static double GetCenterFrequency(int bandNumber)
        {
            if (bandNumber < 1 || bandNumber > Frequencies.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bandNumber),
                    bandNumber,
                    $"Номер полосы должен быть в диапазоне 1..{Frequencies.Length}.");
            }

            return Frequencies[bandNumber - 1];
        }

        /// <summary>
        /// Форматирует подпись полосы, например «10 Гц».
        /// </summary>
        /// <param name="bandNumber">Номер полосы (1–43).</param>
        public static string FormatBand(int bandNumber)
        {
            double frequency = GetCenterFrequency(bandNumber);
            return frequency >= 1000
                ? $"{frequency / 1000:0.###} кГц"
                : $"{frequency:0.###} Гц";
        }
    }
}
