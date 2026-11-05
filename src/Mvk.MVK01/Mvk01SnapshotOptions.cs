namespace Mvk.MVK01
{
    /// <summary>
    /// Режим проверки согласованности снимка канала.
    /// </summary>
    public enum Mvk01SnapshotConsistencyMode
    {
        /// <summary>
        /// Только объединённые чтения, без проверки копий регистров.
        /// </summary>
        Fast,

        /// <summary>
        /// Проверять копии регистров и повторять чтение при расхождении.
        /// </summary>
        Checked
    }

    /// <summary>
    /// Настройки чтения снимков МВК01: объединение запросов, проверка согласованности,
    /// откат на раздельные чтения и батчинг параметров.
    /// </summary>
    public sealed class Mvk01SnapshotOptions
    {
        /// <summary>
        /// Режим проверки согласованности. По умолчанию <see cref="Mvk01SnapshotConsistencyMode.Checked"/>.
        /// </summary>
        public Mvk01SnapshotConsistencyMode Consistency { get; set; } = Mvk01SnapshotConsistencyMode.Checked;

        /// <summary>
        /// Максимальное число попыток чтения канала при расхождении копий.
        /// По умолчанию 2 (первая попытка и один повтор).
        /// </summary>
        public int MaxChannelReadAttempts { get; set; } = 2;

        /// <summary>
        /// Разрешить откат на раздельные чтения, если устройство не принимает
        /// объединённый запрос (ошибка Modbus 0x02). По умолчанию включено.
        /// </summary>
        public bool FallbackToSplitReads { get; set; } = true;

        /// <summary>
        /// Бросать исключение, если после всех попыток снимок остался несогласованным.
        /// По умолчанию выключено: снимок возвращается с <see cref="Mvk01Channel.IsConsistent"/> = false.
        /// </summary>
        public bool ThrowOnInconsistentSnapshot { get; set; }

        /// <summary>
        /// Максимальное число блоков параметров в одном запросе наблюдателя.
        /// Один блок — 16 регистров; допустимо 1–7 (112 регистров ≤ 125).
        /// По умолчанию 7.
        /// </summary>
        public int MaxMeasurementBatchBlocks { get; set; } = 7;
    }
}
