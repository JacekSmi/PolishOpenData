using System;
using PolishOpenData.Geo;
using PolishOpenData.Geo.Nts;
using PolishOpenData.Uldk;

// Runs the NTS adapter under Native AOT; a non-zero exit code means it misbehaves there.
var parcel = new ParcelGeometry
{
    Srid = 2180,
    Wkt = "POLYGON((638000.5 486000.2,638050.5 486000.2,638050.5 486040.2,638000.5 486040.2,638000.5 486000.2))",
};

var geometry = parcel.ToNtsGeometry();
var inside = geometry.Contains(new Pl1992Point(638010, 486010).ToNtsPoint());
var area = geometry.Area;

// The calls the README advertises, so that the claim "works under Native AOT" is backed by this gate.
var other = new ParcelGeometry { Srid = 2180, Wkt = "POLYGON((638025 486020,638100 486020,638100 486100,638025 486100,638025 486020))" }.ToNtsGeometry();
var overlap = geometry.Intersection(other).Area;
var buffered = geometry.Buffer(50).Area;
var distance = geometry.Distance(new Pl1992Point(638200, 486000).ToNtsPoint());

Console.WriteLine($"{geometry.GeometryType} SRID={geometry.SRID} area={area} contains={inside} overlap={overlap} buffer={buffered:F1} distance={distance:F2}");
var ok = geometry.SRID == 2180 && inside && Math.Abs(area - 2000) < 1e-6
    && Math.Abs(overlap - 515.1) < 1e-6 && buffered > area && Math.Abs(distance - 149.5) < 0.01;
return ok ? 0 : 1;
