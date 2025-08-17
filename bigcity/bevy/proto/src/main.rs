// --- IMPORTS ---
use bevy::prelude::*;
use bevy::image::ImageSampler;
use bevy::render::camera::{RenderTarget};
use bevy::render::render_resource::{
    AsBindGroup, Extent3d, ShaderRef, TextureDescriptor, TextureDimension, TextureFormat,
    TextureUsages,
};
use bevy::render::mesh::Mesh2d;
use bevy::sprite::{Material2d, Material2dPlugin, MeshMaterial2d};
use bevy::window::PrimaryWindow;

// --- CONSTANTS ---
const SIMULATION_WIDTH: u32 = 256;
const SIMULATION_HEIGHT: u32 = 256;
const BRUSH_SIZE: i32 = 5;

// --- PARTICLE DEFINITION ---
#[derive(Clone, Copy, PartialEq, Default, Debug)]
enum Particle {
    #[default]
    Air,
    Bedrock,
    Sand,
    Water,
}

impl Particle {
    fn get_color_id(&self) -> f32 {
        match self {
            Particle::Air => 0.0,
            Particle::Bedrock => 0.1,
            Particle::Sand => 0.5,
            Particle::Water => 1.0,
        }
    }
}

// --- MAIN APP ---
fn main() {
    App::new()
        .add_plugins((
            DefaultPlugins.set(WindowPlugin {
                primary_window: Some(Window {
                    title: "Bevy Falling Sand (0.16 Final)".into(),
                    resolution: (
                        SIMULATION_WIDTH as f32 * 4.0,
                        SIMULATION_HEIGHT as f32 * 4.0,
                    )
                        .into(),
                    ..default()
                }),
                ..default()
            }),
            Material2dPlugin::<SimulationMaterial>::default(),
        ))
        .init_resource::<SelectedParticle>()
        .add_systems(Startup, setup)
        .add_systems(
            Update,
            (
                paint_on_texture,
                switch_particle_type,
                ping_pong.after(paint_on_texture),
            ),
        )
        .run();
}

// --- COMPONENTS AND RESOURCES ---

#[derive(Resource)]
struct PingPong {
    read: Handle<Image>,
    write: Handle<Image>,
}

#[derive(Resource, Default)]
struct SelectedParticle(Particle);

#[derive(Asset, AsBindGroup, TypePath, Debug, Clone)]
struct SimulationMaterial {
    #[texture(0)]
    #[sampler(1)]
    source_image: Handle<Image>,
}

impl Material2d for SimulationMaterial {
    fn fragment_shader() -> ShaderRef {
        "shaders/falling_sand.wgsl".into()
    }
}

// --- SYSTEMS ---

fn setup(
    mut commands: Commands,
    mut images: ResMut<Assets<Image>>,
    mut meshes: ResMut<Assets<Mesh>>,
    mut sim_materials: ResMut<Assets<SimulationMaterial>>,
) {
    let size = Extent3d {
        width: SIMULATION_WIDTH,
        height: SIMULATION_HEIGHT,
        ..default()
    };
    let mut image_data = vec![0; (SIMULATION_WIDTH * SIMULATION_HEIGHT * 4) as usize];

    // Create a bedrock floor
    for x in 0..SIMULATION_WIDTH {
        for y in 0..5 {
            let i = ((y * SIMULATION_WIDTH + x) * 4) as usize;
            image_data[i] = (Particle::Bedrock.get_color_id() * 255.0) as u8;
        }
    }

    // --- THIS IS THE CORRECTED PART ---
    let texture_descriptor = TextureDescriptor {
        label: None,
        size,
        dimension: TextureDimension::D2,
        format: TextureFormat::Rgba8UnormSrgb, // Use sRGB format
        mip_level_count: 1,
        sample_count: 1,
        usage: TextureUsages::TEXTURE_BINDING
            | TextureUsages::COPY_DST
            | TextureUsages::RENDER_ATTACHMENT,
        view_formats: &[TextureFormat::Rgba8UnormSrgb], // Use sRGB format
    };
    // --- END OF CORRECTION ---

    let image_a = Image {
        data: Some(image_data.clone()),
        texture_descriptor: texture_descriptor.clone(),
        sampler: ImageSampler::nearest(),
        ..default()
    };

    let image_b = Image {
        data: Some(image_data),
        texture_descriptor,
        sampler: ImageSampler::nearest(),
        ..default()
    };

    let h_image_a = images.add(image_a);
    let h_image_b = images.add(image_b);

    // This camera renders the simulation shader TO a texture.
    commands.spawn((
        Camera2d,
        Camera {
            target: RenderTarget::Image(h_image_b.clone().into()),
            order: -1,
            ..default()
        },
    ));

    // This camera renders the final result TO the screen.
    commands.spawn(Camera2d::default());

    let material = sim_materials.add(SimulationMaterial {
        source_image: h_image_a.clone(),
    });

    let quad_handle = meshes.add(Rectangle::new(size.width as f32, size.height as f32));

    // Spawn a tuple of components for the simulation quad.
    commands.spawn((
        Mesh2d(quad_handle.into()),
        MeshMaterial2d(material),
        Transform::default(),
        Visibility::default(),
    ));

    // The sprite that displays the final texture on screen.
    commands.spawn((
        Sprite {
            image: h_image_b.clone(),
            custom_size: Some(Vec2::new(
                SIMULATION_WIDTH as f32 * 4.0,
                SIMULATION_HEIGHT as f32 * 4.0,
            )),
            ..default()
        },
        Transform::default(),
        Visibility::default(),
    ));

    commands.insert_resource(PingPong {
        read: h_image_a,
        write: h_image_b,
    });
}

fn ping_pong(
    mut ping_pong: ResMut<PingPong>,
    mut sim_materials: ResMut<Assets<SimulationMaterial>>,
    mut sprite_query: Query<&mut Sprite>,
    mut camera_query: Query<&mut Camera>,
) {
    let temp = ping_pong.read.clone();
    ping_pong.read = ping_pong.write.clone();
    ping_pong.write = temp;

    for (_, material) in sim_materials.iter_mut() {
        material.source_image = ping_pong.read.clone();
    }

    for mut cam in camera_query.iter_mut() {
        if cam.order == -1 {
            cam.target = RenderTarget::Image(ping_pong.write.clone().into());
        }
    }

    // Filter for the specific sprite we want to update.
    for mut sprite in sprite_query.iter_mut().filter(|s| s.custom_size.is_some()) {
        sprite.image = ping_pong.write.clone();
    }
}

fn switch_particle_type(
    keys: Res<ButtonInput<KeyCode>>,
    mut selected: ResMut<SelectedParticle>,
) {
    if keys.just_pressed(KeyCode::Digit1) {
        selected.0 = Particle::Sand;
        info!("Switched to Sand");
    }
    if keys.just_pressed(KeyCode::Digit2) {
        selected.0 = Particle::Water;
        info!("Switched to Water");
    }
    if keys.just_pressed(KeyCode::Digit3) {
        selected.0 = Particle::Bedrock;
        info!("Switched to Bedrock");
    }
}

fn paint_on_texture(
    buttons: Res<ButtonInput<MouseButton>>,
    q_window: Query<&Window, With<PrimaryWindow>>,
    q_camera: Query<(&Camera, &GlobalTransform)>,
    mut images: ResMut<Assets<Image>>,
    ping_pong: Res<PingPong>,
    selected_particle: Res<SelectedParticle>,
) {
    if !buttons.pressed(MouseButton::Left) {
        return;
    }
    
    let Ok(window) = q_window.single() else { return };
    let Some((camera, camera_transform)) = q_camera.iter().find(|(c, _)| c.order == 0) else {
        return;
    };

    if let Some(world_pos) = window
        .cursor_position()
        .and_then(|cursor| camera.viewport_to_world(camera_transform, cursor).ok())
        .map(|ray| ray.origin.truncate())
    {
        let texture_pos = (world_pos
            + Vec2::new(
                SIMULATION_WIDTH as f32 / 2.0,
                SIMULATION_HEIGHT as f32 / 2.0,
            ))
        .as_uvec2();

        if let Some(image) = images.get_mut(&ping_pong.read) {
            if let Some(data) = &mut image.data {
                for y_offset in -BRUSH_SIZE..=BRUSH_SIZE {
                    for x_offset in -BRUSH_SIZE..=BRUSH_SIZE {
                        let x = (texture_pos.x as i32 + x_offset) as u32;
                        let y = (texture_pos.y as i32 + y_offset) as u32;

                        if x < SIMULATION_WIDTH && y < SIMULATION_HEIGHT {
                            let i = ((y * SIMULATION_WIDTH + x) * 4) as usize;
                            data[i] = (selected_particle.0.get_color_id() * 255.0) as u8;
                        }
                    }
                }
            }
        }
    }
}