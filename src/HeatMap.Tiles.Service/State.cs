using System;

namespace HeatMap.Tiles.Service
{
    /// <summary>
    /// Represents the state after the last update.
    /// </summary>
    public class State
    {
        /// <summary>
        /// Gets or sets the last timestamp.
        /// </summary>
        public DateTimeOffset TimeStamp { get; set; }
    }
}