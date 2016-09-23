using UnityEngine;
using System.Collections;
using System.Threading.Tasks;

public class TaskTest : MonoBehaviour
{

    private IEnumerator Start()
    {
        UnityTaskScheduler.Initialize();

        var task = Task.Run(() => print("First action."))
            .ContinueWith(t => print("wait for click"))
            .ContinueWith(WaitForClick())
            .ContinueWith(t =>
            {
                print("Second action.");
            })
            .ContinueWith(new WaitForSeconds(1))
            .ContinueWith(t =>
            {
                print("Third action.");
            });
        yield break;
    }

    private IEnumerator WaitForClick()
    {
        while (!clicked)
        {
            yield return null;
        }
    }

    private bool clicked = false;
    private void OnGUI()
    {
        if (GUILayout.Button("Click!"))
        {
            clicked = true;
        }
    }
}
