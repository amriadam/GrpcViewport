using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcViewport.V1;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GrpcViewport.WinForms
{
    public partial class Form1 : Form
    {
        // --- UI ---
        private readonly WebView2 _webView = new WebView2 { Dock = DockStyle.Fill };
        private readonly ToolStrip _toolbar = new ToolStrip();
        private readonly ToolStripButton _addLines = new ToolStripButton("Add lines");
        private readonly ToolStripButton _addPoints = new ToolStripButton("Add point cloud");
        private readonly ToolStripButton _addBigCloud = new ToolStripButton("Add 2M points");
        private readonly ToolStripButton _addBoxes = new ToolStripButton("Add boxes");
        private readonly ToolStripButton _removeLast = new ToolStripButton("Remove last");
        private readonly ToolStripLabel _status = new ToolStripLabel("");
        private readonly StatusStrip _statusBar = new StatusStrip();
        private readonly ToolStripStatusLabel _cameraLabel = new ToolStripStatusLabel("camera: -");

        // --- gRPC ---
        private readonly GrpcChannel _channel;
        private readonly SceneService.SceneServiceClient _scene;
        private readonly Stack<GeometryHandle> _handles = new Stack<GeometryHandle>();

        public Form1()
        {
            InitializeComponent();

            _toolbar.Items.AddRange([
                _addLines,
                _addPoints,
                _addBigCloud,
                _addBoxes,
                _removeLast,
                new ToolStripSeparator(),
                _status]);
            _statusBar.Items.Add(_cameraLabel);

            Controls.Add(_toolbar);    // Dock = Top (ToolStrip default)
            Controls.Add(_statusBar);  // Dock = Bottom (StatusStrip default)
            Controls.Add(_webView);    // Dock = Fill
            _webView.BringToFront();   // Fill takes the space left by Top/Bottom

            // One channel for the whole app lifetime.
            _channel = ViewportClient.CreateChannel();
            _scene = new SceneService.SceneServiceClient(_channel);

            _addLines.Click += async (s, e) => await AddAsync("rings", DemoGeometry.Rings());
            _addPoints.Click += async (s, e) => await AddAsync("points", DemoGeometry.SurfaceScan(100_000));
            _addBigCloud.Click += async (s, e) => await AddAsync("2M points", DemoGeometry.SurfaceScan(2_000_000));
            _addBoxes.Click += async (s, e) => await AddAsync("boxes", DemoGeometry.InstancedBoxes());

            _removeLast.Click += async (s, e) => await RemoveLastAsync();
            Load += async (s, e) => await InitWebViewAsync();
            FormClosed += (s, e) => _channel.Dispose();
        }

        private async Task InitWebViewAsync()
        {
            try
            {
                // WebView2's browser profile. LocalAppData is always writable (Program Files isn't).
                string userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GrpcViewport", "WebView2");

                var env = await CoreWebView2Environment.CreateAsync(null, userData);
                await _webView.EnsureCoreWebView2Async(env);

                // Development: set VIEWER_DEV_URL=http://localhost:5173 to load the page from Vite.
                string dev = Environment.GetEnvironmentVariable("VIEWER_DEV_URL");
                _webView.Source = new Uri(string.IsNullOrWhiteSpace(dev) ? "http://127.0.0.1:8080/" : dev);
            }
            catch (Exception ex)
            {
                MessageBox.Show("WebView2 failed: " + ex.Message, "GrpcViewport");
            }
        }

        private async Task AddAsync(string label, AddGeometryRequest request)
        {
            SetButtonsEnabled(false);
            UseWaitCursor = true;
            _status.Text = "Loading " + label + "…";

            var watch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                GeometryHandle handle = await _scene.AddGeometryAsync(request);
                _handles.Push(handle);
                _status.Text = "added #" + handle.Id + " (" + request.Name + ", " + watch.ElapsedMilliseconds + " ms)";
            }
            catch (RpcException ex)
            {
                _status.Text = ex.StatusCode + ": " + ex.Status.Detail;
            }
            finally
            {
                UseWaitCursor = false;
                SetButtonsEnabled(true);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _addLines.Enabled = enabled;
            _addPoints.Enabled = enabled;
            _addBigCloud.Enabled = enabled;
            _addBoxes.Enabled = enabled;
            _removeLast.Enabled = enabled;
        }

        private async Task RemoveLastAsync()
        {
            if (_handles.Count == 0) return;
            GeometryHandle handle = _handles.Pop();
            try
            {
                await _scene.RemoveGeometryAsync(new RemoveGeometryRequest { Handle = handle });
                _status.Text = "removed #" + handle.Id;
            }
            catch (RpcException ex)
            {
                _status.Text = ex.StatusCode + ": " + ex.Status.Detail;
            }
        }
    }
}