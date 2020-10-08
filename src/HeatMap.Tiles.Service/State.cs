using System;

namespace HeatMap.Tiles.Service
{
    /// <summary>
    /// Represents the state after the last update.
    /// </summary>
    public class State
    {
        /// <summary>
        /// Gets or sets the last processed contribution.
        /// </summary>
        public long LastContributionId { get; set; }
    }
}