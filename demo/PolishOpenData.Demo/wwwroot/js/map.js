// index.html loads this module on its own, so the map is on screen before .NET starts; App.razor imports the same module afterwards.
const search = document.getElementById('search');

// If .NET never starts (a blocked or failed download) the box would say "Warming up…" for ever, so after 30 s it says so. It is set before
// anything else can throw. A start that is only very slow still ends in enableSearch, which puts the normal state back.
const startTimer = setTimeout(() => { search.placeholder = 'Could not start. Reload the page.'; }, 30000);

const POLAND = [[49.0, 14.1], [54.9, 24.2]];
const ORTHOPHOTO = 'https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMTS/StandardResolution?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=ORTOFOTOMAPA&STYLE=default&FORMAT=image/jpeg&TILEMATRIXSET=EPSG:3857&TILEMATRIX=EPSG:3857:{z}&TILEROW={y}&TILECOL={x}';

// Quarter zoom steps let the first view show all of Poland on a phone, where level 6 is a little too wide (Poland is 460 px wide there).
// Leaflet draws the tile level nearest to the zoom, so 5.75 and 6.25 both draw level 6. The tile layer needs the same minimum as the
// map: below its own minZoom it drops every tile, and it compares that with the fractional zoom, not the tile level.
// Capping the first view at 6.25 keeps it to level 6 tiles on a desktop too.
const MIN_ZOOM = 5.75;
const map = L.map('map', { zoomControl: false, zoomSnap: 0.25, minZoom: MIN_ZOOM, maxZoom: 20 });
map.fitBounds(POLAND, { padding: [8, 8], maxZoom: 6.25 });
// Quarter steps were only for that first framing. From here the zoom buttons and the wheel move in whole levels, where the tiles are
// drawn at their native size (a quarter step would stretch a tile level by 1.19 and soften the photo). Leaflet reads this on every zoom.
map.options.zoomSnap = 1;
L.control.zoom({ position: 'bottomright' }).addTo(map);

L.tileLayer(ORTHOPHOTO, {
    className: 'ortho',
    minZoom: MIN_ZOOM,
    maxNativeZoom: 19,
    maxZoom: 20,
    // The service answers HTTP 200 with an XML exception outside its tile matrix, so never ask outside Poland.
    bounds: [[48.8, 13.8], [55.0, 24.4]],
    attribution: 'Ortofotomapa &copy; <a href="https://www.geoportal.gov.pl/">GUGiK</a>',
}).addTo(map);

// Called from App.razor once the .NET runtime is up.
export function enableSearch() {
    clearTimeout(startTimer);
    search.disabled = false;
    search.placeholder = 'Search an address';
}
