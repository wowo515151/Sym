namespace SymBlazor.Graphing;

public static class GraphFigures
{
    public static Dictionary<string, object?> LineFigure(
        string title,
        string xTitle,
        string yTitle,
        IReadOnlyList<double> xValues,
        IReadOnlyList<double?> yValues,
        string traceName)
    {
        return Figure(
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "scatter",
                    ["mode"] = "lines",
                    ["name"] = traceName,
                    ["x"] = xValues,
                    ["y"] = yValues,
                    ["line"] = new Dictionary<string, object?>
                    {
                        ["color"] = "#4a90e2",
                        ["width"] = 3
                    }
                }
            ],
            Layout(title, xTitle, yTitle));
    }

    public static Dictionary<string, object?> Parametric2DFigure(
        string title,
        IReadOnlyList<double?> xValues,
        IReadOnlyList<double?> yValues,
        string traceName)
    {
        return Figure(
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "scatter",
                    ["mode"] = "lines",
                    ["name"] = traceName,
                    ["x"] = xValues,
                    ["y"] = yValues,
                    ["line"] = new Dictionary<string, object?>
                    {
                        ["color"] = "#89d1b6",
                        ["width"] = 3
                    }
                }
            ],
            Layout(title, "x", "y", true));
    }

    public static Dictionary<string, object?> PolarFigure(
        string title,
        IReadOnlyList<double?> radii,
        IReadOnlyList<double?> thetaDegrees)
    {
        return Figure(
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "scatterpolar",
                    ["mode"] = "lines",
                    ["r"] = radii,
                    ["theta"] = thetaDegrees,
                    ["line"] = new Dictionary<string, object?>
                    {
                        ["color"] = "#f2b36f",
                        ["width"] = 3
                    }
                }
            ],
            new Dictionary<string, object?>
            {
                ["title"] = new Dictionary<string, object?> { ["text"] = title },
                ["paper_bgcolor"] = "rgba(0,0,0,0)",
                ["plot_bgcolor"] = "rgba(0,0,0,0)",
                ["font"] = new Dictionary<string, object?> { ["color"] = "#edf5ff" },
                ["margin"] = new Dictionary<string, object?> { ["l"] = 40, ["r"] = 40, ["t"] = 54, ["b"] = 40 },
                ["polar"] = new Dictionary<string, object?>
                {
                    ["bgcolor"] = "rgba(255,255,255,0.02)",
                    ["radialaxis"] = new Dictionary<string, object?> { ["gridcolor"] = "rgba(122,164,224,0.22)" },
                    ["angularaxis"] = new Dictionary<string, object?> { ["gridcolor"] = "rgba(122,164,224,0.18)" }
                }
            });
    }

    public static Dictionary<string, object?> SurfaceFigure(
        string title,
        IReadOnlyList<double> xValues,
        IReadOnlyList<double> yValues,
        IReadOnlyList<IReadOnlyList<double?>> zValues)
    {
        return Figure(
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "surface",
                    ["x"] = xValues,
                    ["y"] = yValues,
                    ["z"] = zValues,
                    ["colorscale"] = "Viridis",
                    ["showscale"] = true
                }
            ],
            SceneLayout(title, "x", "y", "z"));
    }

    public static Dictionary<string, object?> Parametric3DFigure(
        string title,
        IReadOnlyList<double?> xValues,
        IReadOnlyList<double?> yValues,
        IReadOnlyList<double?> zValues)
    {
        return Figure(
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "scatter3d",
                    ["mode"] = "lines",
                    ["x"] = xValues,
                    ["y"] = yValues,
                    ["z"] = zValues,
                    ["line"] = new Dictionary<string, object?>
                    {
                        ["color"] = "#89d1b6",
                        ["width"] = 7
                    }
                }
            ],
            SceneLayout(title, "x", "y", "z"));
    }

    public static Dictionary<string, object?> VectorFieldFigure(
        string title,
        IReadOnlyList<double?> xValues,
        IReadOnlyList<double?> yValues)
    {
        return Figure(
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "scatter",
                    ["mode"] = "lines",
                    ["x"] = xValues,
                    ["y"] = yValues,
                    ["line"] = new Dictionary<string, object?>
                    {
                        ["color"] = "#89d1b6",
                        ["width"] = 2
                    }
                }
            ],
            Layout(title, "x", "y", true));
    }

    private static Dictionary<string, object?> Figure(
        IReadOnlyList<Dictionary<string, object?>> data,
        Dictionary<string, object?> layout)
    {
        return new Dictionary<string, object?>
        {
            ["data"] = data,
            ["layout"] = layout,
            ["config"] = new Dictionary<string, object?>
            {
                ["responsive"] = true,
                ["displaylogo"] = false
            }
        };
    }

    private static Dictionary<string, object?> Layout(string title, string xTitle, string yTitle, bool equalScale = false)
    {
        var yAxis = new Dictionary<string, object?>
        {
            ["title"] = yTitle,
            ["gridcolor"] = "rgba(122,164,224,0.18)",
            ["zerolinecolor"] = "rgba(122,164,224,0.35)"
        };

        if (equalScale)
        {
            yAxis["scaleanchor"] = "x";
            yAxis["scaleratio"] = 1;
        }

        return new Dictionary<string, object?>
        {
            ["title"] = new Dictionary<string, object?> { ["text"] = title },
            ["paper_bgcolor"] = "rgba(0,0,0,0)",
            ["plot_bgcolor"] = "rgba(255,255,255,0.02)",
            ["font"] = new Dictionary<string, object?> { ["color"] = "#edf5ff" },
            ["margin"] = new Dictionary<string, object?> { ["l"] = 60, ["r"] = 20, ["t"] = 54, ["b"] = 56 },
            ["xaxis"] = new Dictionary<string, object?>
            {
                ["title"] = xTitle,
                ["gridcolor"] = "rgba(122,164,224,0.18)",
                ["zerolinecolor"] = "rgba(122,164,224,0.35)"
            },
            ["yaxis"] = yAxis
        };
    }

    private static Dictionary<string, object?> SceneLayout(string title, string xTitle, string yTitle, string zTitle)
    {
        return new Dictionary<string, object?>
        {
            ["title"] = new Dictionary<string, object?> { ["text"] = title },
            ["paper_bgcolor"] = "rgba(0,0,0,0)",
            ["font"] = new Dictionary<string, object?> { ["color"] = "#edf5ff" },
            ["margin"] = new Dictionary<string, object?> { ["l"] = 0, ["r"] = 0, ["t"] = 54, ["b"] = 0 },
            ["scene"] = new Dictionary<string, object?>
            {
                ["xaxis"] = new Dictionary<string, object?> { ["title"] = xTitle, ["gridcolor"] = "rgba(122,164,224,0.15)" },
                ["yaxis"] = new Dictionary<string, object?> { ["title"] = yTitle, ["gridcolor"] = "rgba(122,164,224,0.15)" },
                ["zaxis"] = new Dictionary<string, object?> { ["title"] = zTitle, ["gridcolor"] = "rgba(122,164,224,0.15)" },
                ["bgcolor"] = "rgba(255,255,255,0.02)"
            }
        };
    }
}
