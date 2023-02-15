﻿using AssetStudio;
using System;

namespace Helper
{
    internal class LogHelper : ILogger
    {
        public void Log(LoggerEvent loggerEvent, string message)
        {
            Console.WriteLine($"{loggerEvent}: {message}");
        }
    }
}
