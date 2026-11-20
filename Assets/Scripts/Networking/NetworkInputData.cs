using Fusion;
using UnityEngine;

namespace EscapeRoom.Net
{
    /// <summary>Input que viaja de cada jugador al host. Dirección ya en coordenadas de mundo (relativa a la cámara).</summary>
    public struct NetworkInputData : INetworkInput
    {
        public Vector3 Direction;
    }
}
