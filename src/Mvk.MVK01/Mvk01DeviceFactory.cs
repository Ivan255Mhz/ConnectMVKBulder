using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mvk.Modbus.Pool;

namespace Mvk.MVK01
{
    /// <summary>
    /// Создаёт и кэширует типизированные фасады для устройств пула.
    /// </summary>
    public sealed class Mvk01DeviceFactory : IMvk01DeviceFactory
    {
        private readonly IMvkDevicePool pool;
        private readonly Encoding textEncoding;
        private readonly ConcurrentDictionary<string, Mvk01Device> devices =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Создаёт фабрику.
        /// </summary>
        /// <param name="pool">Пул устройств.</param>
        /// <param name="textEncoding">
        /// Кодировка строк устройства; по умолчанию <see cref="Encoding.Latin1"/>.
        /// </param>
        public Mvk01DeviceFactory(IMvkDevicePool pool, Encoding? textEncoding = null)
        {
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            this.textEncoding = textEncoding ?? Encoding.Latin1;
        }

        /// <inheritdoc/>
        public IMvk01Device GetDevice(string id)
        {
            if (devices.TryGetValue(id, out Mvk01Device? cached))
            {
                return cached;
            }

            IMvkDevice device = pool.GetDevice(id);
            Mvk01Device facade = devices.GetOrAdd(device.Settings.Id, _ => new Mvk01Device(device, textEncoding));
            return facade;
        }

        /// <inheritdoc/>
        public bool TryGetDevice(string id, out IMvk01Device device)
        {
            if (!pool.TryGetDevice(id, out _))
            {
                device = null!;
                return false;
            }

            device = GetDevice(id);
            return true;
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<IMvk01Device> Devices =>
            pool.Devices.Select(device => GetDevice(device.Settings.Id)).ToList();
    }
}
