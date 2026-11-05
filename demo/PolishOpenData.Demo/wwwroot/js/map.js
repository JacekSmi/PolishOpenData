// index.html loads this module on its own, so the map is on screen before .NET starts; App.razor imports the same module afterwards.
const POLAND = [[49.0, 14.1], [54.9, 24.2]];
const ORTHOPHOTO = 'https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMTS/StandardResolution?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=ORTOFOTOMAPA&STYLE=default&FORMAT=image/jpeg&TILEMATRIXSET=EPSG:3857&TILEMATRIX=EPSG:3857:{z}&TILEROW={y}&TILECOL={x}';

// Quarter zoom steps let the first view show all of Poland on a phone, where level 6 is a little too wide (Poland is 460 px wide there).
// Leaflet draws the tile level nearest to the zoom, so 5.75 and 6.25 both draw level 6. The tile layer needs the same minimum as the
// map: below its own minZoom it drops every tile, and it compares that with the fractional zoom, not the tile level.
// Capping the first view at 6.25 keeps it to level 6 tiles on a desktop too.
const MIN_ZOOM = 5.75;
const map = L.map('map', { zoomControl: false, zoomSnap: 0.25, minZoom: MIN_ZOOM, maxZoom: 20 });
map.fitBounds(POLAND, { padding: [8, 8], maxZoom: 6.25 });
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

const search = document.getElementById('search');

// Called from App.razor once the .NET runtime is up.
export function enableSearch() {
    search.disabled = false;
    search.placeholder = 'Search an address';
}
