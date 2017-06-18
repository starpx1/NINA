﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NINA.Utility {
    class Mediator {
        private Mediator() { }
        private static Mediator _instance = new Mediator();
        private static object lockObject = new object();

        public static Mediator Instance {
            get {
                lock (lockObject) {
                    if (_instance == null)
                        _instance = new Mediator();
                    return _instance;
                }
            }
        }

        Dictionary<MediatorMessages, List<Action<Object>>> _internalList
            = new Dictionary<MediatorMessages, List<Action<Object>>>();

        Dictionary<AsyncMediatorMessages, List<Func<object, Task>>> _internalAsyncList
            = new Dictionary<AsyncMediatorMessages, List<Func<object, Task>>>();

        public void Register(Action<Object> callback,
              MediatorMessages message) {
            if(!_internalList.ContainsKey(message)) {
                _internalList[message] = new List<Action<object>>();
            }
            _internalList[message].Add(callback);
        }

        public void Notify(MediatorMessages message, object args) {
            if (_internalList.ContainsKey(message)) {
                //forward the message to all listeners
                foreach (Action<object> callback in _internalList[message]) {
                    callback(args);
                }
            }
        }

        public void RegisterAsync(Func<object, Task> callback,
              AsyncMediatorMessages message) {
            if (!_internalAsyncList.ContainsKey(message)) {
                _internalAsyncList[message] = new List<Func<object, Task>>();
            }
            _internalAsyncList[message].Add(callback);
        }
        

        public async Task NotifyAsync(AsyncMediatorMessages message, object args) {
            if (_internalAsyncList.ContainsKey(message)) {
                //forward the message to all listeners
                foreach (Func<object, Task> callback in _internalAsyncList[message]) {
                    await callback(args);
                }
            }
        }

    }
    public enum MediatorMessages {
        StatusUpdate = 1,
        IsExposingUpdate = 2,
        TelescopeChanged = 3,
        CameraChanged = 4,
        FilterWheelChanged = 5,
        ImageChanged = 6,
        AutoStrechChanged = 7,
        DetectStarsChanged = 8,
        PlateSolveResultChanged = 9,
        PlateSolveExposureDurationChanged = 10,
        PlateSolveBinningChanged = 11,
        PlateSolveFilterChanged = 12,
        Sync = 13
    };

    public enum AsyncMediatorMessages {
        StartSequence = 1,
        CaptureImage = 2,
        BlindSolveWithCapture = 3,
        Sync = 4,
        SyncTelescopeAndReslew = 5
    }

}