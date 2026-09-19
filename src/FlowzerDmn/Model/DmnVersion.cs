namespace FlowzerDmn.Model;

/// <summary>
/// Fassung der DMN-Spezifikation, aus der eine Datei stammt.
/// Die Fassung wird nur erkannt und mitgefuehrt; ausgewertet werden alle Fassungen gleich.
/// </summary>
public enum DmnVersion
{
    /// <summary>Der Modell-Namensraum gehoert zu keiner bekannten DMN-Fassung.</summary>
    Unknown = 0,

    /// <summary>DMN 1.1 (<c>http://www.omg.org/spec/DMN/20151101/dmn.xsd</c>).</summary>
    Dmn11,

    /// <summary>DMN 1.2 (<c>https://www.omg.org/spec/DMN/20180521/MODEL/</c>).</summary>
    Dmn12,

    /// <summary>DMN 1.3 (<c>https://www.omg.org/spec/DMN/20191111/MODEL/</c>).</summary>
    Dmn13,

    /// <summary>DMN 1.4 (<c>https://www.omg.org/spec/DMN/20211108/MODEL/</c>).</summary>
    Dmn14
}
