using System;

namespace Mvk.Modbus.Pool
{
    /// <summary>
    /// Данные события изменения состояния устройства.
    /// </summary>
    public sealed class MvkDeviceStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Создаёт данные события.
        /// </summary>
        /// <param name="device">Устройство.</param>
        /// <param name="oldState">Предыдущее состояние.</param>
        /// <param name="newState">Новое состояние.</param>
        /// <param name="error">Ошибка, вызвавшая переход (может отсутствовать).</param>
        public MvkDeviceStateChangedEventArgs(
            IMvkDevice device,
            MvkDeviceState oldState,
            MvkDeviceState newState,
            MvkModbusException? error = null)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            OldState = oldState;
            NewState = newState;
            Error = error;
        }

        /// <summary>
        /// Устройство, у которого изменилось состояние.
        /// </summary>
        public IMvkDevice Device { get; }

        /// <summary>
        /// Предыдущее состояние.
        /// </summary>
        public MvkDeviceState OldState { get; }

        /// <summary>
        /// Новое состояние.
        /// </summary>
        public MvkDeviceState NewState { get; }

        /// <summary>
        /// Ошибка, вызвавшая переход (может отсутствовать).
        /// </summary>
        public MvkModbusException? Error { get; }
    }
}
