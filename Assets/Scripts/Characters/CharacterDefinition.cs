using UnityEngine;

namespace EscapeRoom
{
    [CreateAssetMenu(menuName = "Escape Room/Character", fileName = "Character")]
    public class CharacterDefinition : ScriptableObject
    {
        public string displayName = "Personaje";
        [Tooltip("Modelo con su Animator y su ciclo de animación ya asignados.")]
        public GameObject visualPrefab;
        public RuntimeAnimatorController controller;
    }
}
