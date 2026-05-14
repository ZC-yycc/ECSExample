using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.InputSystem;

namespace ECSExample
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PlayerMoveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var move_input = float2.zero;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed) move_input.y += 1f;
                if (Keyboard.current.sKey.isPressed) move_input.y -= 1f;
                if (Keyboard.current.dKey.isPressed) move_input.x += 1f;
                if (Keyboard.current.aKey.isPressed) move_input.x -= 1f;
            }

            if (math.lengthsq(move_input) > 1f)
            {
                move_input = math.normalize(move_input);
            }

            float delta_time = SystemAPI.Time.DeltaTime;
            float speed = 10f;
            if (SystemAPI.HasSingleton<PlayerData>())
            {
                speed = SystemAPI.GetSingleton<PlayerData>().move_speed;
            }

            foreach (var transform in SystemAPI.Query<RefRW<LocalTransform>>()
                         .WithAll<PlayerTag>())
            {
                float3 new_pos = transform.ValueRO.Position;
                new_pos.x += move_input.x * speed * delta_time;
                new_pos.z += move_input.y * speed * delta_time;
                transform.ValueRW.Position = new_pos;
            }
        }
    }
}
