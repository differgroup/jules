using Godot;
using System;

public partial class Hud : CanvasLayer
{
	private Label _fpsLabel;
	private Label _debugLabel;
		
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_fpsLabel = GetNode<Label>("FpsLabel"); // GetNode("FpsLabel") as Label;
		_debugLabel = GetNode<Label>("DebugLabel");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		// var fps = Engine.GetFramesPerSecond();
		var fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
		_fpsLabel.Text = $"FPS: {fps}";
	}
	
	public void Debug(string text)
	{
		_debugLabel.Text = $"Debug: {text}";
	}
}
