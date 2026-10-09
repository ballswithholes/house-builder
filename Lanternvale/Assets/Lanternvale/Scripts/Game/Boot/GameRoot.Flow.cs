// Game-flow part of the bootstrap: creates the GameFlow hub (on the persistent GameRoot object) and the
// IMGUI host. The game starts in the main menu: GameFlow builds a title backdrop diorama in its Start().
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class GameRoot
    {
        partial void OnBoot()
        {
            // GameFlow first: UI screens are constructed by UiRoot.Awake and may subscribe to GameFlow.Instance.
            if (GameFlow.Instance == null) gameObject.AddComponent<GameFlow>();
            UiRoot.Create();
            GameAudio.Init();
            SetMode(GameMode.MainMenu);
        }
    }
}
