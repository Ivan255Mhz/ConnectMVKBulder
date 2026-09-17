using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mvk.MVK01;
using Mvk.MVK01.DependencyInjection;
using Mvk.Modbus.DependencyInjection;

namespace Mvk.Sample.Console;

using System;

using Mvk.MVK01;


internal static class Program
{
    private static async Task Main(string[] args)
    {

       

        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
        // порт 502, SlaveId 1, BigEndian, таймауты и реконнект — по умолчанию

        Mvk01DeviceInfo info = await mvk.ReadDeviceInfoAsync();
        Console.WriteLine($"ПО: {info.FirmwareVersion}, серийный: {info.SerialNumber}");

        Mvk01Channel channel = await mvk.ReadChannelAsync(1);

        while (channel != null) 
        {
            Console.WriteLine($"Пикфактор - {channel.PeakFactor}");
            Console.Clear();
        }


        

    }
}
