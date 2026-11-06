using System;
using System.Collections.Generic;
using Mvk.MVK01.Polling;

namespace Mvk.MVK01
{
    /// <summary>
    /// Один непрерывный запрос чтения регистров.
    /// </summary>
    internal readonly record struct Mvk01ReadChunk(ushort Address, ushort Quantity);

    /// <summary>
    /// Три непрерывных диапазона блока канала. Структура, чтобы не выделять
    /// массив на каждый снимок.
    /// </summary>
    internal readonly struct Mvk01ChannelChunks
    {
        public Mvk01ChannelChunks(
            Mvk01ReadChunk first,
            Mvk01ReadChunk second,
            Mvk01ReadChunk third)
        {
            First = first;
            Second = second;
            Third = third;
        }

        public Mvk01ReadChunk First { get; }

        public Mvk01ReadChunk Second { get; }

        public Mvk01ReadChunk Third { get; }
    }

    /// <summary>
    /// Группа параметров, читаемых одним запросом.
    /// </summary>
    /// <param name="StartNumber">Первый номер параметра в группе.</param>
    /// <param name="EndNumber">Последний номер параметра в группе.</param>
    /// <param name="Indexes">Индексы исходных элементов опроса в порядке поступления.</param>
    internal sealed record Mvk01PollBatch(int StartNumber, int EndNumber, IReadOnlyList<int> Indexes);

    /// <summary>
    /// Планировщик объединённых чтений МВК01. Все диапазоны кратны двум
    /// и не превышают лимит Modbus в 125 регистров.
    /// </summary>
    internal static class Mvk01ReadPlanner
    {
        internal const int ChannelFirstChunkQuantity = Mvk01RegisterMap.RmsSetOffset;
        internal const int ChannelSecondChunkOffset = Mvk01RegisterMap.RmsSetOffset;
        internal const int ChannelSecondChunkQuantity = 0x7C;
        internal const int ChannelThirdChunkOffset =
            ChannelSecondChunkOffset + ChannelSecondChunkQuantity;
        internal const int ChannelThirdChunkQuantity = 0x46;
        private const int DeviceInfoQuantity =
            Mvk01RegisterMap.DspIndicators + 6 - Mvk01RegisterMap.DeviceSerialNumber;
        private const int ChannelConfigQuantity = Mvk01RegisterMap.ChannelMinDcVoltageOffset + 2;
        private const int MaxMeasurementBlocks = 7;

        /// <summary>
        /// Три запроса, покрывающих блок расчётных параметров канала (0x2000–0x2121).
        /// Границы полей не пересекают границы чанков.
        /// </summary>
        public static Mvk01ChannelChunks ChannelDataChunks(int channel)
        {
            ushort baseAddress = Mvk01RegisterMap.ChannelData(channel);

            return new Mvk01ChannelChunks(
                new Mvk01ReadChunk(baseAddress, ChannelFirstChunkQuantity),
                new Mvk01ReadChunk(
                    (ushort)(baseAddress + ChannelSecondChunkOffset),
                    ChannelSecondChunkQuantity),
                new Mvk01ReadChunk(
                    (ushort)(baseAddress + ChannelThirdChunkOffset),
                    ChannelThirdChunkQuantity));
        }

        /// <summary>
        /// Один запрос общих сведений об устройстве (0x1000–0x1025).
        /// </summary>
        public static Mvk01ReadChunk DeviceInfoChunk()
        {
            return new(Mvk01RegisterMap.DeviceSerialNumber, DeviceInfoQuantity);
        }

        /// <summary>
        /// Один запрос конфигурации канала (0x00–0x53 от базы канала).
        /// </summary>
        public static Mvk01ReadChunk ChannelConfigChunk(int channel)
        {
            return new(Mvk01RegisterMap.ChannelConfig(channel), ChannelConfigQuantity);
        }

        /// <summary>
        /// Один запрос диапазона параметров по номерам.
        /// </summary>
        public static Mvk01ReadChunk MeasurementRangeChunk(int startNumber, int endNumber)
        {
            if (startNumber < 1
                || endNumber > Mvk01RegisterMap.MaxMeasurementNumber
                || endNumber < startNumber)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startNumber),
                    $"Диапазон параметров {startNumber}..{endNumber} вне 1..{Mvk01RegisterMap.MaxMeasurementNumber}.");
            }

            int count = endNumber - startNumber + 1;
            if (count > MaxMeasurementBlocks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(endNumber),
                    $"Диапазон {startNumber}..{endNumber} превышает {MaxMeasurementBlocks} блоков на запрос.");
            }

            return new(
                Mvk01RegisterMap.MeasurementByNumber(startNumber),
                (ushort)(count * Mvk01RegisterMap.MeasurementByNumberStride));
        }

        /// <summary>
        /// Разбивает список наблюдаемых параметров на группы, которые помещаются
        /// в один запрос (не более <paramref name="maxBlocks"/> блоков по 16 регистров).
        /// Порядок элементов сохраняется.
        /// </summary>
        public static IReadOnlyList<Mvk01PollBatch> CreatePollBatches(
            IReadOnlyList<Mvk01PollItem> items,
            int maxBlocks)
        {
            ArgumentNullException.ThrowIfNull(items);

            int limit = Math.Clamp(maxBlocks, 1, MaxMeasurementBlocks);
            List<Mvk01PollBatch> batches = [];
            int index = 0;

            while (index < items.Count)
            {
                int start = (int)items[index].Number;
                int end = start;
                List<int> indexes = [index];
                int next = index + 1;

                while (next < items.Count)
                {
                    int candidate = (int)items[next].Number;
                    int candidateStart = Math.Min(start, candidate);
                    int candidateEnd = Math.Max(end, candidate);

                    if (candidateEnd - candidateStart + 1 > limit)
                    {
                        break;
                    }

                    start = candidateStart;
                    end = candidateEnd;
                    indexes.Add(next);
                    next++;
                }

                batches.Add(new Mvk01PollBatch(start, end, indexes));
                index = next;
            }

            return batches;
        }
    }
}
