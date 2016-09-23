using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace System.Threading.Tasks
{
    public class UnityTaskScheduler : MonoBehaviour
    {
        private static UnityTaskScheduler instance;
        private static object syncRoot = new Object();
        private List<Action> actions = new List<Action>();
        private List<Action> excutingActions = new List<Action>();

        public static UnityTaskScheduler Instance
        {
            get
            {
                if (instance == null)
                {
                    throw new Exception("not initialized!");
                }
                return instance;
            }
        }

        public static void Initialize()
        {
            if (instance == null)
            {
                lock (syncRoot)
                {
                    var go = new GameObject("_Task");
                    instance = go.AddComponent<UnityTaskScheduler>();
                }
            }
        }

        private IEnumerator Start()
        {
            while (true)
            {
                lock (syncRoot)
                {
                    excutingActions.Clear();
                    excutingActions.AddRange(actions);
                    actions.Clear();
                }
                foreach (var action in excutingActions)
                {
                    action();
                }
                yield return null;
            }
        }

        public void Post(IEnumerator coroutine)
        {
            Post(() => StartCoroutine(coroutine));
        }

        public void Post(Action a)
        {
            lock (syncRoot)
            {
                actions.Add(a);
            }
        }
    }
}
