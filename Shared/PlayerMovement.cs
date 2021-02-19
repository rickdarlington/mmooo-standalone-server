using DarkRift.Server;
using Vector2 = System.Numerics.Vector2;

namespace MmoooPlugin.Shared
{
    public static class PlayerMovement
    {
        public static Vector2 MovePlayer(NetworkingData.PlayerInputData input, Vector2 position, float timeStep)
        {
            //TODO not sure why we're getting a null reference occasionally here...  punt for now, but investigate
            if (input.Keyinputs == null)
            {
                return position;
            }

            Vector2 moveDirection = Vector2.Zero;

            bool w = input.Keyinputs[0];
            bool a = input.Keyinputs[1];
            bool s = input.Keyinputs[2];
            bool d = input.Keyinputs[3];
            bool space = input.Keyinputs[4];

            if (w || a || s || d || space)
            {
                if (w) moveDirection.Y = 1;
                if (s) moveDirection.Y = -1;

                if (a) moveDirection.X = -1;
                if (d) moveDirection.X = 1;
            }

            position += moveDirection * timeStep;

            return position;
        }
    }
}