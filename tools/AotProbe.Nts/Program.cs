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

Console.WriteLine($"{geometry.GeometryType} SRID={geometry.SRID} area={area} contains={inside}");
return geometry.SRID == 2180 && inside && Math.Abs(area - 2000) < 1e-6 ? 0 : 1;
