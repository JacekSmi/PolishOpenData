using System;
using System.Text;
using System.Xml.Linq;
using PolishOpenData.Bir.Wire;

namespace PolishOpenData.Bir.Tests;

public class BirRequestTests
{
    private static readonly Uri TestEndpoint = new("https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc");

    private const string PublAction = "http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/";

    // A table instead of a [Theory]: the enum is internal, and a public test method cannot take it as a parameter (CS0051).
    [Fact]
    public void Search_field_maps_to_the_element_name()
    {
        var cases = new[]
        {
            (Field: BirSearchField.Regon, Element: "Regon"),
            (Field: BirSearchField.Nip, Element: "Nip"),
            (Field: BirSearchField.Krs, Element: "Krs"),
            (Field: BirSearchField.Regony9zn, Element: "Regony9zn"),
            (Field: BirSearchField.Regony14zn, Element: "Regony14zn"),
            (Field: BirSearchField.Nipy, Element: "Nipy"),
            (Field: BirSearchField.Krsy, Element: "Krsy"),
        };
        Assert.Equal(typeof(BirSearchField).GetEnumNames().Length, cases.Length);   // a new member needs a case here

        foreach (var (field, elementName) in cases)
        {
            var request = BirRequest.Search(TestEndpoint, field, "123");

            var document = XDocument.Parse(Encoding.UTF8.GetString(request.Body));
            XNamespace dataContract = "http://CIS/BIR/PUBL/2014/07/DataContract";
            var child = Assert.Single(document.Descendants(), e => e.Name.Namespace == dataContract);
            Assert.Equal(elementName, child.Name.LocalName);
            Assert.Equal("123", child.Value);
            Assert.Equal(BirOperation.DaneSzukajPodmioty, request.Operation);
        }
    }

    [Fact]
    public void Login_and_logout_carry_operation_and_action()
    {
        var login = BirRequest.Login(TestEndpoint, "dummy-key-not-a-secret");
        Assert.Equal(BirOperation.Zaloguj, login.Operation);
        Assert.Equal(TestEndpoint, login.Endpoint);
        Assert.Equal(PublAction + "Zaloguj", login.Action);
        Assert.Contains("<ns:pKluczUzytkownika>dummy-key-not-a-secret</ns:pKluczUzytkownika>", Encoding.UTF8.GetString(login.Body), StringComparison.Ordinal);

        var logout = BirRequest.Logout(TestEndpoint, "0123456789abcdefghij");
        Assert.Equal(BirOperation.Wyloguj, logout.Operation);
        Assert.Equal(PublAction + "Wyloguj", logout.Action);
        Assert.Contains("<ns:pIdentyfikatorSesji>0123456789abcdefghij</ns:pIdentyfikatorSesji>", Encoding.UTF8.GetString(logout.Body), StringComparison.Ordinal);

        var report = BirRequest.Report(TestEndpoint, "000331501", "BIR12OsPrawna");
        Assert.Equal(BirOperation.DanePobierzPelnyRaport, report.Operation);
        Assert.Equal(PublAction + "DanePobierzPelnyRaport", report.Action);
        Assert.Contains("<ns:pRegon>000331501</ns:pRegon>", Encoding.UTF8.GetString(report.Body), StringComparison.Ordinal);
        Assert.Contains("<ns:pNazwaRaportu>BIR12OsPrawna</ns:pNazwaRaportu>", Encoding.UTF8.GetString(report.Body), StringComparison.Ordinal);

        var getValue = BirRequest.GetValue(TestEndpoint, "StanDanych");
        Assert.Equal(BirOperation.GetValue, getValue.Operation);
        Assert.Equal("http://CIS/BIR/2014/07/IUslugaBIR/GetValue", getValue.Action);

        var search = BirRequest.Search(TestEndpoint, BirSearchField.Nip, "5261040828");
        Assert.Equal(PublAction + "DaneSzukajPodmioty", search.Action);
        Assert.Equal(TestEndpoint, search.Endpoint);
    }

    [Fact]
    public void Requests_reject_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => BirRequest.Login(null!, "key"));
        Assert.Throws<ArgumentNullException>(() => BirRequest.Login(TestEndpoint, null!));
        Assert.Throws<ArgumentNullException>(() => BirRequest.Logout(TestEndpoint, null!));
        Assert.Throws<ArgumentNullException>(() => BirRequest.Search(TestEndpoint, BirSearchField.Nip, null!));
        Assert.Throws<ArgumentNullException>(() => BirRequest.Report(TestEndpoint, null!, "BIR12OsPrawna"));
        Assert.Throws<ArgumentNullException>(() => BirRequest.Report(TestEndpoint, "000331501", null!));
        Assert.Throws<ArgumentNullException>(() => BirRequest.GetValue(TestEndpoint, null!));
    }

    [Fact]
    public void Undefined_search_field_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BirRequest.Search(TestEndpoint, (BirSearchField)99, "1"));
    }

    [Fact]
    public void Operations_know_their_result_element_and_namespace()
    {
        const string publ = "http://CIS/BIR/PUBL/2014/07";
        var cases = new[]
        {
            (Operation: BirOperation.Zaloguj, Name: "Zaloguj", Result: "ZalogujResult", Namespace: publ),
            (Operation: BirOperation.Wyloguj, Name: "Wyloguj", Result: "WylogujResult", Namespace: publ),
            (Operation: BirOperation.DaneSzukajPodmioty, Name: "DaneSzukajPodmioty", Result: "DaneSzukajPodmiotyResult", Namespace: publ),
            (Operation: BirOperation.DanePobierzPelnyRaport, Name: "DanePobierzPelnyRaport", Result: "DanePobierzPelnyRaportResult", Namespace: publ),
            (Operation: BirOperation.GetValue, Name: "GetValue", Result: "GetValueResult", Namespace: "http://CIS/BIR/2014/07"),
        };
        Assert.Equal(typeof(BirOperation).GetEnumNames().Length, cases.Length);

        foreach (var (operation, name, resultElement, resultNamespace) in cases)
        {
            Assert.Equal(name, BirOperations.Name(operation));
            Assert.Equal(resultElement, BirOperations.ResultElement(operation));
            Assert.Equal(resultNamespace, BirOperations.ResultNamespace(operation));
        }
    }

    [Fact]
    public void Undefined_operation_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BirOperations.Name((BirOperation)99));
    }
}
