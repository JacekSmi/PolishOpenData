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

// ---- The place on the map, driven from .NET (Map/MapInterop.cs) -------------------------------------------------------------------
// This part only draws shapes and forwards clicks. Everything that comes from the page (the outline, the numbers, the colours) is data:
// it is never turned into HTML, and nothing here opens a popup or a tooltip. What a dot means is shown by the page's own card.

const WHITE = '#fff';
const RED = '#ff1f3d';
const INK = '#1c2530';
const HEX = /^#[0-9a-f]{6}$/i;

// The circle and the outline sit in their own pane under the dots: the dots are drawn on a canvas in the overlay pane, the shapes
// on an SVG, and two layers of one pane stack by the order they were created in, which would depend on what was drawn first.
const placePane = map.createPane('place');
placePane.style.zIndex = 390;
const placeLayer = L.layerGroup().addTo(map);

// One canvas for every dot (about 1,000 of them), one click handler for the whole group; `index` on a dot's options says which one it was.
const dotRenderer = L.canvas({ tolerance: 3 });
const dotLayer = L.featureGroup().addTo(map);
dotLayer.on('click', e => forward('OnDotClick', e.layer.options.index));
// The numbers of the dots that stand for several sales; a canvas cannot draw text, so these are plain markers above the canvas.
const countLayer = L.layerGroup().addTo(map);

let dotNet = null;

function forward(method, ...args) {
    // The only way this fails is a callback that outlived the page's reference to .NET (a click while the page is being torn down).
    dotNet?.invokeMethodAsync(method, ...args).catch(error => console.warn(method + ' failed', error));
}

function onMapClick(e) {
    // A dot's click does not get here: its markers have bubblingMouseEvents off.
    const { lat, lng } = e.latlng.wrap();
    forward('OnMapClick', lat, lng);
}

// Starts forwarding clicks to the .NET object. Safe to call again: the handler is taken off before it is put on, so a click never counts twice.
export function attach(dotNetRef) {
    map.off('click', onMapClick);
    dotNet = dotNetRef;
    map.on('click', onMapClick);
    // A click picks a place, so the two clicks of a double click would pick it twice and then zoom away from it. Zoom is the buttons, the wheel and a pinch.
    map.doubleClickZoom.disable();
}

// Stops forwarding and takes the place off the map. The map, its tiles and its view are left as they are.
export function detach() {
    map.off('click', onMapClick);
    map.doubleClickZoom.enable();
    dotNet = null;
    clear();
}

export function clear() {
    placeLayer.clearLayers();
    dotLayer.clearLayers();
    countLayer.clearLayers();
}

// center: [lat, lon]; parcelGeoJson: a GeoJSON geometry as text, or null; dots: [{ lat, lon, color, counted, count, index }].
export function showPlace(center, radiusMeters, parcelGeoJson, dots) {
    clear();
    const middle = L.latLng(center);

    // A faint dark line under the white dashes keeps the circle visible over pale roofs and paving.
    L.circle(middle, { pane: 'place', radius: radiusMeters, color: INK, weight: 4, opacity: 0.3, fill: false, interactive: false }).addTo(placeLayer);
    L.circle(middle, { pane: 'place', radius: radiusMeters, color: WHITE, weight: 2, dashArray: '6 6', fill: false, interactive: false }).addTo(placeLayer);
    if (parcelGeoJson) {
        try {
            L.geoJSON(JSON.parse(parcelGeoJson), { pane: 'place', style: { color: RED, weight: 3, fillColor: RED, fillOpacity: 0.12 }, interactive: false }).addTo(placeLayer);
        } catch (error) {
            // An outline that cannot be read is left out; the circle and the dots still show.
            console.warn('The parcel outline could not be drawn', error);
        }
    }

    drawDots(dots);
    // The circle's box rather than circle.getBounds(), which needs a view. zoomSnap is 1, so the fit may be a level looser than the box, never tighter.
    map.fitBounds(middle.toBounds(2 * radiusMeters), { padding: [24, 24] });
}

// Replaces the dots (and their numbers) and leaves the circle, the outline and the view alone.
export function showDots(dots) {
    drawDots(dots);
}

function drawDots(dots) {
    dotLayer.clearLayers();
    countLayer.clearLayers();
    // Bottom to top: single sales, then groups with a number; within each the excluded dots below the counted ones. A number is a marker, above every dot,
    // so its own dot has to be above the single dots, or the number would float over a neighbour.
    const rank = d => (d.count > 1 ? 2 : 0) + (d.counted ? 1 : 0);
    for (const dot of [...dots].sort((a, b) => rank(a) - rank(b))) {
        const position = [dot.lat, dot.lon];
        const color = HEX.test(dot.color) ? dot.color : '#6f7a89';
        const several = dot.count > 1;
        // Counted: filled with the class colour. The white edge keeps it readable on any photo.
        // Not counted: small and hollow, filled only when it has to carry a number.
        const style = dot.counted
            ? { radius: several ? 10 : 7, fillColor: color, fillOpacity: 0.95, color: WHITE, weight: 1.5, opacity: 0.9 }
            : { radius: several ? 9 : 5, fillColor: WHITE, fill: several, fillOpacity: 0.85, color, weight: 2, opacity: 1 };
        if (!dot.counted) {
            // A white casing under the grey ring, so the hollow dot shows on dark trees as well as on pale roofs.
            L.circleMarker(position, { renderer: dotRenderer, radius: style.radius, color: WHITE, weight: 5, opacity: 0.85, fill: false, interactive: false }).addTo(dotLayer);
        }
        L.circleMarker(position, { ...style, renderer: dotRenderer, bubblingMouseEvents: false, index: dot.index }).addTo(dotLayer);

        if (several) {
            // textContent, not HTML: the number is data. Dark text on a light dot, white on a dark one.
            const label = document.createElement('span');
            label.textContent = String(dot.count);
            label.style.color = dot.counted && luma(color) < 140 ? WHITE : INK;
            L.marker(position, {
                icon: L.divIcon({ className: 'sale-count', html: label, iconSize: [2 * style.radius, 2 * style.radius] }),
                interactive: false,
                keyboard: false,
            }).addTo(countLayer);
        }
    }
}

function luma(hex) {
    const channel = i => parseInt(hex.slice(1 + 2 * i, 3 + 2 * i), 16);
    return 0.2126 * channel(0) + 0.7152 * channel(1) + 0.0722 * channel(2);
}
