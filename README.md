# heatmap-tiles

A service to general a global heatmap of cycling data.  :bicyclist: :bicyclist: :bicyclist:

This service generates the data behind the map that is then generate by the [Mapbox GL JS](https://docs.mapbox.com/mapbox-gl-js/api/) heatmap layer. A demo is available [here](https://bikedataproject.github.io/heatmap-experiment/).

<img src="https://github.com/bikedataproject/heatmap-tiles/raw/master/docs/screenshot1.png" width="400"/><img src="https://github.com/bikedataproject/heatmap-tiles/raw/master/docs/screenshot2.png" width="400"/><img src="https://github.com/bikedataproject/heatmap-tiles/raw/master/docs/screenshot3.png" width="400"/><img src="https://github.com/bikedataproject/heatmap-tiles/raw/master/docs/screenshot4.png" width="400"/>

## How does this work?

This service keeps a tiny heatmap per user and updates those track-by-track. Then it uses those user-heatmaps to update the global heatmap.

1. Take the new unprocessed tracks.  
  a. Update the heatmaps of the users of those tracks.  
  b. Keep the tile numbers at zoom level `14` that were modified while doing this.
2. Update the global heatmap  
  a. Rebuild the zoom level `14` modified tiles by:
    1. Adding up all data of all heatmaps of all users in the tile.     
    2. Exclude all pixels where we have less than `n` users.  
  b. Update the higher zoom levels by rebuilding the modified tiles.   
