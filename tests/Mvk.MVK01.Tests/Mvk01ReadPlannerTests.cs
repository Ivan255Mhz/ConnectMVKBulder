using System.Collections.Generic;
using System.Linq;
using Mvk.MVK01.Polling;
using Xunit;

namespace Mvk.MVK01.Tests
{
    public sealed class Mvk01ReadPlannerTests
    {
        [Fact]
        public void ChannelDataChunks_AreEvenWithinLimitAndContiguous()
        {
            for (int channel = 1; channel <= Mvk01RegisterMap.MaxChannels; channel++)
            {
                IReadOnlyList<Mvk01ReadChunk> chunks = Mvk01ReadPlanner.ChannelDataChunks(channel);

                Assert.Equal(3, chunks.Count);

                foreach (Mvk01ReadChunk chunk in chunks)
                {
                    Assert.Equal(0, chunk.Address % 2);
                    Assert.Equal(0, chunk.Quantity % 2);
                    Assert.InRange(chunk.Quantity, 1, 125);
                }

                ushort baseAddress = Mvk01RegisterMap.ChannelData(channel);

                Assert.Equal(baseAddress, chunks[0].Address);
                Assert.Equal(chunks[0].Address + chunks[0].Quantity, chunks[1].Address);
                Assert.Equal(chunks[1].Address + chunks[1].Quantity, chunks[2].Address);
                Assert.Equal(
                    baseAddress + 0x122,
                    chunks[2].Address + chunks[2].Quantity);
            }
        }

        [Fact]
        public void DeviceInfoChunk_CoversWholeInfoBlock()
        {
            Mvk01ReadChunk chunk = Mvk01ReadPlanner.DeviceInfoChunk();

            Assert.Equal(Mvk01RegisterMap.DeviceSerialNumber, chunk.Address);
            Assert.Equal(38, chunk.Quantity);
            Assert.Equal(0, chunk.Address % 2);
            Assert.Equal(0, chunk.Quantity % 2);
            Assert.True(chunk.Quantity <= 125);
        }

        [Fact]
        public void ChannelConfigChunk_CoversWholeConfigBlock()
        {
            Mvk01ReadChunk chunk = Mvk01ReadPlanner.ChannelConfigChunk(3);

            Assert.Equal(Mvk01RegisterMap.ChannelConfig(3), chunk.Address);
            Assert.Equal(84, chunk.Quantity);
            Assert.Equal(0, chunk.Address % 2);
            Assert.Equal(0, chunk.Quantity % 2);
            Assert.True(chunk.Quantity <= 125);
        }

        [Fact]
        public void MeasurementRangeChunk_UsesNumberStride()
        {
            Mvk01ReadChunk chunk = Mvk01ReadPlanner.MeasurementRangeChunk(48, 54);

            Assert.Equal(Mvk01RegisterMap.MeasurementByNumber(48), chunk.Address);
            Assert.Equal(7 * 16, chunk.Quantity);
        }

        [Fact]
        public void CreatePollBatches_SevenAdjacentItems_IsSingleBatch()
        {
            Mvk01PollItem[] items = Enumerable
                .Range(49, 7)
                .Select(number => new Mvk01PollItem((Mvk01MeasurementNumber)number))
                .ToArray();

            IReadOnlyList<Mvk01PollBatch> batches = Mvk01ReadPlanner.CreatePollBatches(items, maxBlocks: 7);

            Mvk01PollBatch batch = Assert.Single(batches);
            Assert.Equal(49, batch.StartNumber);
            Assert.Equal(55, batch.EndNumber);
            Assert.Equal(Enumerable.Range(0, 7), batch.Indexes);
        }

        [Fact]
        public void CreatePollBatches_EightAdjacentItems_SplitsIntoTwoBatches()
        {
            Mvk01PollItem[] items = Enumerable
                .Range(49, 8)
                .Select(number => new Mvk01PollItem((Mvk01MeasurementNumber)number))
                .ToArray();

            IReadOnlyList<Mvk01PollBatch> batches = Mvk01ReadPlanner.CreatePollBatches(items, maxBlocks: 7);

            Assert.Equal(2, batches.Count);
            Assert.Equal((49, 55), (batches[0].StartNumber, batches[0].EndNumber));
            Assert.Equal((56, 56), (batches[1].StartNumber, batches[1].EndNumber));
        }

        [Fact]
        public void CreatePollBatches_PreservesInputOrder()
        {
            Mvk01PollItem[] items =
            [
                new(Mvk01MeasurementNumber.Rms10To1000Acceleration),
                new(Mvk01MeasurementNumber.RmsX25kHzAcceleration)
            ];

            IReadOnlyList<Mvk01PollBatch> batches = Mvk01ReadPlanner.CreatePollBatches(items, maxBlocks: 7);

            Assert.Equal(2, batches.Count);
            Assert.Equal((int)Mvk01MeasurementNumber.Rms10To1000Acceleration, batches[0].StartNumber);
            Assert.Equal(0, batches[0].Indexes[0]);
            Assert.Equal((int)Mvk01MeasurementNumber.RmsX25kHzAcceleration, batches[1].StartNumber);
            Assert.Equal(1, batches[1].Indexes[0]);
        }
    }
}
