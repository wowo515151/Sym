#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TimeSeriesResearch;
using TimeSeriesResearch.Forecasting;
using TimeSeriesResearch.Forecasters;

namespace TimeSeriesResearch.Tuner
{
    /// <summary>
    /// Simple model tuner that loads data/train.parquet or falls back to CSV, splits into train/validation,
    /// performs grid search over a small set of hyperparameters for several forecasters and reports MAE/RMSE.
    /// To avoid adding dependencies, uses SimpleParquetReader which is a stub and will usually result in no rows.
    /// </summary>
    public static class ModelTuner
    {
        public static void Main(string[] args)
        {
            string repoRoot = Directory.GetCurrentDirectory();
            string[] candidates = { Path.Combine("data", "train.parquet"), Path.Combine("data","time_series.csv"), "data.csv" };
            double[] series = Array.Empty<double>();

            foreach (var c in candidates)
            {
                var path = Path.Combine(repoRoot, c);
                if (File.Exists(path))
                {
                    try
                    {
                        if (path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
                        {
                            var rows = SimpleParquetReader.Read(path).ToArray();
                            // try to extract first numeric column if any
                            var vals = new List<double>();
                            foreach (var r in rows)
                            {
                                if (r.Count == 0) continue;
                                var v = r.Values.FirstOrDefault();
                                if (v is double d) vals.Add(d);
                                else if (v is int iv) vals.Add(iv);
                                else if (v is long lv) vals.Add(lv);
                                else if (v is float fv) vals.Add(fv);
                                else if (v is string s && double.TryParse(s, out double dv)) vals.Add(dv);
                            }
                            if (vals.Count > 0) { series = vals.ToArray(); break; }
                        }
                        else
                        {
                            var lines = File.ReadAllLines(path);
                            var vals = new List<double>();
                            foreach (var ln in lines)
                            {
                                var s = ln.Trim(); if (string.IsNullOrEmpty(s)) continue;
                                if (double.TryParse(s, out double v)) { vals.Add(v); continue; }
                                var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 0 && double.TryParse(parts[0], out double v2)) vals.Add(v2);
                            }
                            if (vals.Count > 0) { series = vals.ToArray(); break; }
                        }
                    }
                    catch { }
                }
            }

            if (series.Length == 0)
            {
                Console.WriteLine("No data found in data/train.parquet or CSV candidates. Exiting.");
                return;
            }

            int n = series.Length;
            int valSize = Math.Max(1, (int)(n * 0.2));
            int trainSize = n - valSize;
            var train = series.Take(trainSize).ToArray();
            var val = series.Skip(trainSize).ToArray();

            Console.WriteLine($"Loaded series length={n}. Train={train.Length}, Val={val.Length}");

            // Define model grids
            var results = new List<(string Name, string Config, double Mae, double Rmse)>();

            static double CalculateMAE(double[] actual, double[] predicted)
            {
                if (actual == null || predicted == null) throw new ArgumentNullException();
                int n = Math.Min(actual.Length, predicted.Length);
                if (n == 0) return double.PositiveInfinity;
                double sum = 0.0;
                for (int i = 0; i < n; i++) sum += Math.Abs(actual[i] - predicted[i]);
                return sum / n;
            }

            // OLS-AR grid
            for (int p = 1; p <= 5; p += 2)
            {
                try
                {
                    var ar = new OlsArForecaster();
                    ar.Fit(train, p);
                    var seed = train.Skip(Math.Max(0, train.Length - p)).ToArray();
                    var preds = ar.Forecast(seed, val.Length);
                    var mae = CalculateMAE(val, preds);
                    var rmse = Metrics.CalculateRMSE(val, preds);
                    results.Add(("OLS-AR", $"p={p}", mae, rmse));
                }
                catch { }
            }

            // KNN grid
            foreach (var w in new[] { 6, 12 })
            foreach (var k in new[] { 3, 5 })
            {
                try
                {
                    var knn = new KnnForecaster(w, k);
                    knn.Fit(train);
                    var preds = knn.Forecast(val.Length);
                    var mae = CalculateMAE(val, preds);
                    var rmse = Metrics.CalculateRMSE(val, preds);
                    results.Add(("KNN", $"w={w},k={k}", mae, rmse));
                }
                catch { }
            }

            // KernelRegression grid
            foreach (var s in new[] { 0.5, 1.0 })
            {
                try
                {
                    var kr = new KernelRegressionForecaster(s);
                    kr.Fit(train);
                    var preds = kr.Predict(val);
                    var mae = CalculateMAE(val, preds);
                    var rmse = Metrics.CalculateRMSE(val, preds);
                    results.Add(("KernelRegression", $"sigma={s}", mae, rmse));
                }
                catch { }
            }

            // NeuralStacking grid
            foreach (var h in new[] { 8, 16 })
            {
                try
                {
                    var ns = new NeuralStackingForecaster(arOrder:3, knnWindow:12, knnK:5, krSigma:1.0, hidden:h);
                    ns.Fit(train);
                    var preds = ns.Forecast(val.Length);
                    var mae = CalculateMAE(val, preds);
                    var rmse = Metrics.CalculateRMSE(val, preds);
                    results.Add(("NeuralStacking", $"hidden={h}", mae, rmse));
                }
                catch { }
            }

            // TransformerResidual grid
            foreach (var ctx in new[] { 8, 12 })
            {
                try
                {
                    var tr = new TransformerResidualForecaster(arOrder:3, contextLength:ctx);
                    tr.Fit(train);
                    var preds = tr.Forecast(val.Length);
                    var mae = CalculateMAE(val, preds);
                    var rmse = Metrics.CalculateRMSE(val, preds);
                    results.Add(("TransformerResidual", $"ctx={ctx}", mae, rmse));
                }
                catch { }
            }

            // pick best by MAE and RMSE
            if (results.Count == 0) { Console.WriteLine("No models produced results."); return; }
            var bestMae = results.OrderBy(r => r.Mae).First();
            var bestRmse = results.OrderBy(r => r.Rmse).First();

            Console.WriteLine("Model tuning summary:");
            Console.WriteLine("All evaluated models:");
            foreach (var r in results.OrderBy(x => x.Rmse))
            {
                Console.WriteLine($"{r.Name} ({r.Config}) -> MAE={r.Mae:F4}, RMSE={r.Rmse:F4}");
            }

            Console.WriteLine($"\nBest MAE: {bestMae.Name} ({bestMae.Config}) MAE={bestMae.Mae:F4} RMSE={bestMae.Rmse:F4}");
            Console.WriteLine($"Best RMSE: {bestRmse.Name} ({bestRmse.Config}) MAE={bestRmse.Mae:F4} RMSE={bestRmse.Rmse:F4}");
        }
    }
}
