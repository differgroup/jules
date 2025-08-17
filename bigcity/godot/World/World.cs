using Godot;
using System;
using System.Collections.Generic;

public partial class World : Node2D
{
	// Voxel Types (using constants for readability)
	private const byte PIXEL_AIR = 0;
	private const byte PIXEL_SAND = 1;
	private const byte PIXEL_STONE = 2;
	private const byte PIXEL_EMITTER = 3;

 	private const int CHUNK_SIZE = 64; // User-specified 64x64 chunks
	private int width = 1280;
	private int height = 720;
	private int chunksX;
	private int chunksY;
	
	// A dictionary to hold the StaticBody2D for each chunk
	private Dictionary<Vector2I, StaticBody2D> chunkCollisionBodies = new Dictionary<Vector2I, StaticBody2D>();
	// A set to track which chunks need their collision regenerated
	private HashSet<Vector2I> _dirtyChunks = new HashSet<Vector2I>();
	
	private PackedScene _hudScene;
	private Hud _hudInstance;
	private PackedScene _playerScene;
	private CharacterBody2D _playerInstance;
	
	private Image worldImage;
	private ImageTexture worldTexture;
	private Sprite2D worldSprite;
	private StaticBody2D collisionBody;
	private Random _random = new Random();
	private byte[] pixels;
	private byte[] colorData;

	// 1. Create a Color Map
	private Dictionary<byte, Color> colorMap = new Dictionary<byte, Color>()
	{
		{ PIXEL_AIR, new Color(0, 0, 0, 0) }, // Transparent for air
		{ PIXEL_SAND, new Color("#c2b280") }, // A nice sand color
		{ PIXEL_STONE, new Color("#888888") }, // Gray for stone
		{ PIXEL_EMITTER, new Color("#ff00ff") }
	};

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Calculate chunk grid dimensions
		chunksX = width / CHUNK_SIZE;
		chunksY = height / CHUNK_SIZE;

		pixels = new byte[width * height];
		colorData = new byte[width * height * 4];

		// Create a parent node to keep the scene tree clean
		var collisionParent = new Node2D { Name = "CollisionChunks" };
		AddChild(collisionParent);
		
		// Create a StaticBody2D for every chunk at the start
		for (int y = 0; y < chunksY; y++)
		{
			for (int x = 0; x < chunksX; x++)
			{
				var chunkCoord = new Vector2I(x, y);
				var newCollisionBody = new StaticBody2D
				{
					Name = $"Chunk_{x}_{y}",
					CollisionLayer = 2
				};
				chunkCollisionBodies[chunkCoord] = newCollisionBody;
				collisionParent.AddChild(newCollisionBody);
			}
		}
		
		GenerateWorld();
		
		// --- INITIAL COLLISION GENERATION ---
		// Generate collision for ALL chunks once at the beginning
		for (int y = 0; y < chunksY; y++)
		{
			for (int x = 0; x < chunksX; x++)
			{
				GenerateCollisionForChunk(new Vector2I(x, y));
			}
		}

		// 2. Change Image format to support color with transparency
		worldImage = Image.Create(width, height, false, Image.Format.Rgba8);
		worldTexture = ImageTexture.CreateFromImage(worldImage);
		worldSprite = new Sprite2D() { Centered = false, Texture = worldTexture };
		AddChild(worldSprite);

		// Crucially, update the texture once right after generating the world
		// to show the initial state.
		UpdateTexture();

		// Use the ResourceLoader to load the player scene file
		_playerScene = GD.Load<PackedScene>("res://Player/player.tscn");
		_hudScene = GD.Load<PackedScene>("res://Hud/hud.tscn");

		// Now, instantiate and add the player
		SpawnPlayer();

		_hudInstance = _hudScene.Instantiate<Hud>();
		AddChild(_hudInstance);
	}

	// Helper function to get and set pixel data
	public byte GetPixel(int x, int y)
	{
		if (x >= 0 && x < width && y >= 0 && y < height)
		{
			return pixels[y * width + x];
		}
		
		return PIXEL_AIR; // if out of bounds
	}

	public void SetPixel(int x, int y, byte value)
	{
		if (x < 0 || x >= width || y < 0 || y >= height) return;
		
		int index = y * width + x;
		if (pixels[index] != value) // Only dirty the chunk if the pixel actually changes
		{
			pixels[index] = value;
			// Calculate which chunk this pixel is in and add it to the dirty set
			_dirtyChunks.Add(new Vector2I(x / CHUNK_SIZE, y / CHUNK_SIZE));
		}
	}
	
	private void RegenerateDirtyChunkCollisions()
	{
		foreach (var chunkCoord in _dirtyChunks)
		{
			// 1. Get the body for this chunk
			var body = chunkCollisionBodies[chunkCoord];
			
			// 2. Clear out all of its old collision shapes
			foreach (var child in body.GetChildren())
			{
				child.QueueFree();
			}

			// 3. Regenerate new, correct collision shapes for this chunk
			GenerateCollisionForChunk(chunkCoord);
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	
// This is a no-op right now, but we keep it for later.
	public override void _PhysicsProcess(double delta)
	{
		SimulateStep();
		ProcessPlayerSandCollision();
		RegenerateDirtyChunkCollisions();
		UpdateTexture();
		
		_hudInstance.Debug($"Collision Shapes: {collisionBody.GetChildCount()}");
		
		// For debug, show number of dirty chunks this frame
		_hudInstance.Debug($"Dirty Chunks: {_dirtyChunks.Count}");
		// Clear the set for the next frame
		_dirtyChunks.Clear();
	}
	
	private void SimulateStep()
	{
		// Iterate from the bottom-up to correctly simulate falling
		for (int y = height - 2; y >= 0; y--)
		{
			// Randomize scan direction to prevent biased sliding
			bool scanLeftToRight = _random.Next(0, 2) == 0;
			if (scanLeftToRight)
			{
				for (int x = 0; x < width; x++)
				{
					UpdatePixel(x, y);
				}
			}
			else
			{
				for (int x = width - 1; x >= 0; x--)
				{
					UpdatePixel(x, y);
				}
			}
		}
	}

	private void UpdateTexture()
	{
		for (int i = 0; i < pixels.Length; i++)
		{
			byte pixelType = pixels[i];
			Color color = colorMap[pixelType];
			int colorIndex = i * 4;
			colorData[colorIndex] = (byte)(color.R * 255);
			colorData[colorIndex + 1] = (byte)(color.G * 255);
			colorData[colorIndex + 2] = (byte)(color.B * 255);
			colorData[colorIndex + 3] = (byte)(color.A * 255);
		}
		
		// Use Image.SetData directly for a slight performance boost
		worldImage.SetData(width, height, false, Image.Format.Rgba8, colorData);
		worldTexture.Update(worldImage);
	}
	
	private void UpdatePixel(int x, int y)
	{
		byte currentPixel = GetPixel(x, y);

		if (currentPixel == PIXEL_EMITTER)
		{
			// If the space directly below is empty, create a sand pixel.
			if (GetPixel(x, y + 1) == PIXEL_AIR)
			{
				SetPixel(x, y + 1, PIXEL_SAND);
			}
			// The emitter itself never moves, so we just return.
			return;
		} else if (currentPixel == PIXEL_SAND)
		{
			// Rule 1: Fall straight down if possible
			if (GetPixel(x, y + 1) == PIXEL_AIR)
			{
				SetPixel(x, y, PIXEL_AIR);
				SetPixel(x, y + 1, PIXEL_SAND);
				return; // Pixel has moved, we're done with it for this frame
			}

			// Rule 2: Fall diagonally
			bool canGoLeft = GetPixel(x - 1, y + 1) == PIXEL_AIR;
			bool canGoRight = GetPixel(x + 1, y + 1) == PIXEL_AIR;

			if (canGoLeft && canGoRight)
			{
				// If both are possible, pick one randomly
				if (_random.Next(0, 2) == 0)
				{
					SetPixel(x, y, PIXEL_AIR);
					SetPixel(x - 1, y + 1, PIXEL_SAND);
				}
				else
				{
					SetPixel(x, y, PIXEL_AIR);
					SetPixel(x + 1, y + 1, PIXEL_SAND);
				}
			}
			else if (canGoLeft)
			{
				SetPixel(x, y, PIXEL_AIR);
				SetPixel(x - 1, y + 1, PIXEL_SAND);
			}
			else if (canGoRight)
			{
				SetPixel(x, y, PIXEL_AIR);
				SetPixel(x + 1, y + 1, PIXEL_SAND);
			}
		}
	}
	
	private void ProcessPlayerSandCollision()
	{
		// Ensure the player has been created before running this
		if (_playerInstance == null) return;
	
		// Get the player's collision shape in world coordinates
		// NOTE: This assumes the player has one and only one CollisionShape2D child.
		var collisionShapeNode = _playerInstance.GetNode<CollisionShape2D>("CollisionShape2D");
		if (collisionShapeNode == null)
		{
			// Add a safety check in case the node was renamed or deleted.
			GD.PrintErr("Player is missing its CollisionShape2D node!");
			return;
		}

		// Get the actual Shape resource from the node
		var shape = collisionShapeNode.Shape;

		// We need to calculate the bounding box differently for each shape type
		Rect2 playerGlobalBounds;
		Vector2 shapeSize;

		// --- CASE 1: The shape is a CapsuleShape2D ---
		if (shape is CapsuleShape2D capsule)
		{
			// Manually calculate the bounding box of the capsule
			float width = capsule.Radius * 2;
			float height = capsule.Height + capsule.Radius * 2; // The height is the central part + the two half-circles
			shapeSize = new Vector2(width, height);
		}
		// --- CASE 2: The shape is a RectangleShape2D ---
		else if (shape is RectangleShape2D rect)
		{
			// For a rectangle, the size is just its Size property
			shapeSize = rect.Size;
		}
		else
		{
			// If the shape is something else (like a circle or custom polygon),
			// we'll just skip the logic for now.
			return;
		}

		// Now calculate the global bounding box using the calculated size.
		// The top-left corner is the player's center minus half the size.
		Vector2 topLeft = _playerInstance.GlobalPosition - (shapeSize / 2);
		playerGlobalBounds = new Rect2(topLeft, shapeSize);
	
		// Iterate through a bounding box of pixels that MIGHT be touching the player
		for (int y = (int)playerGlobalBounds.Position.Y; y < (int)playerGlobalBounds.End.Y; y++)
		{
			for (int x = (int)playerGlobalBounds.Position.X; x < (int)playerGlobalBounds.End.X; x++)
			{
				// If we find a sand pixel inside the player's rectangle
				if (GetPixel(x, y) == PIXEL_SAND)
				{
					// Find a nearby empty spot to move the sand to.
					// This is a simple "displace" logic.
					// Check right, left, and above for an empty pixel.
					if (GetPixel(x + 2, y) == PIXEL_AIR)
					{
						SetPixel(x, y, PIXEL_AIR);
						SetPixel(x + 2, y, PIXEL_SAND);
					}
					else if (GetPixel(x - 2, y) == PIXEL_AIR)
					{
						SetPixel(x, y, PIXEL_AIR);
						SetPixel(x - 2, y, PIXEL_SAND);
					}
					else if (GetPixel(x, y - 2) == PIXEL_AIR)
					{
						SetPixel(x, y, PIXEL_AIR);
						SetPixel(x, y - 2, PIXEL_SAND);
					}
				}
			}
		}
	}
	
	private void GenerateWorld()
	{
		double stoneLevel = height * 0.6;		
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				// First, create the stone base
				if (y > stoneLevel)
				{
					SetPixel(x, y, PIXEL_STONE);
				}
				// Everything else is air
				else
				{
					SetPixel(x, y, PIXEL_AIR);
				}
			}
		}
		
		// Place it in the middle horizontally, and a bit above the stone line.
		int emitterX = width / 2;
		int emitterY = (int)stoneLevel - 160;
		SetPixel(emitterX, emitterY, PIXEL_EMITTER);
		
		// NOTE: For better caves, you would add cellular automata smoothing steps here.
	}
	
	private void SpawnPlayer()
	{
		// Create an instance of the player scene
		_playerInstance = (CharacterBody2D)_playerScene.Instantiate();

		// Set the player's starting position
		_playerInstance.Position = new Vector2( 50, 50 );
		_playerInstance.ZIndex = 1;
		
		// Add the player instance to the World scene
		AddChild(_playerInstance);
	}
	
	private void GenerateCollisionForChunk(Vector2I chunkCoord)
	{
		var body = chunkCollisionBodies[chunkCoord];
		var processedInChunk = new bool[CHUNK_SIZE * CHUNK_SIZE];

		int startWorldX = chunkCoord.X * CHUNK_SIZE;
		int startWorldY = chunkCoord.Y * CHUNK_SIZE;

		for (int y = 0; y < CHUNK_SIZE; y++)
		{
			for (int x = 0; x < CHUNK_SIZE; x++)
			{
				int worldX = startWorldX + x;
				int worldY = startWorldY + y;
				
				// --- Treat both Sand and Stone as collidable ---
				byte pixelType = GetPixel(worldX, worldY);
				bool isSolid = (pixelType == PIXEL_STONE || pixelType == PIXEL_SAND);

				if (isSolid && !processedInChunk[y * CHUNK_SIZE + x])
				{
					// Greedy meshing logic, but constrained to this chunk's bounds
					int rectWidth = 0;
					while (x + rectWidth < CHUNK_SIZE && 
						   (GetPixel(worldX + rectWidth, worldY) == PIXEL_STONE || GetPixel(worldX + rectWidth, worldY) == PIXEL_SAND) && 
						   !processedInChunk[y * CHUNK_SIZE + (x + rectWidth)])
					{
						rectWidth++;
					}

					int rectHeight = 1;
					bool canExpandDown = true;
					while (canExpandDown && y + rectHeight < CHUNK_SIZE)
					{
						for (int i = 0; i < rectWidth; i++)
						{
							byte nextPixelType = GetPixel(worldX + i, worldY + rectHeight);
							if (nextPixelType != PIXEL_STONE && nextPixelType != PIXEL_SAND)
							{
								canExpandDown = false;
								break;
							}
						}
						if (canExpandDown) rectHeight++;
					}

					// Create and position the shape
					var shape = new RectangleShape2D { Size = new Vector2(rectWidth, rectHeight) };
					var collisionShape = new CollisionShape2D
					{
						Shape = shape,
						Position = new Vector2(worldX, worldY) + (shape.Size / 2)
					};
					body.AddChild(collisionShape);

					// Mark pixels as processed
					for (int h = 0; h < rectHeight; h++)
						for (int w = 0; w < rectWidth; w++)
							processedInChunk[(y + h) * CHUNK_SIZE + (x + w)] = true;
				}
			}
		}
	}
}
