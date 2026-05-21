using UnityEngine;
using UnityEngine.InputSystem;

public class SpaceshipCommands : MonoBehaviour
{
    bool _menuOpen;
    Vector2 _menuPos;

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            Vector2 mousePos = mouse.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject)
            {
                _menuOpen = true;
                _menuPos = mousePos;
            }
            else if (_menuOpen)
            {
                _menuOpen = false;
            }
        }
    }

    void OnGUI()
    {
        if (!_menuOpen) return;

        Vector2 screen = new Vector2(_menuPos.x, Screen.height - _menuPos.y);
        float w = 160, h = 30;
        string[] options = { "Move To", "Orbit Target", "Attack", "Stop" };

        for (int i = 0; i < options.Length; i++)
        {
            Rect r = new Rect(screen.x, screen.y + i * h, w, h);
            if (GUI.Button(r, options[i]))
            {
                OnCommand(options[i]);
                _menuOpen = false;
            }
        }
    }

    void OnCommand(string cmd)
    {
        Debug.Log($"[Spaceship] Command: {cmd}");
        // TODO: implement per-command logic
    }
}
