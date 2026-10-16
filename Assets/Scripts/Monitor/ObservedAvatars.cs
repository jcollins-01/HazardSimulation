using System.Collections.Generic;
using Normal.Realtime;
using UnityEngine;

/// <summary>
/// Shared helpers for observer clients (the bird's-eye <see cref="MonitorCamera"/> and the
/// <see cref="CommanderCab"/>): list the VR users in a stable order, hear them without
/// distance falloff, and tell who is talking.
/// </summary>
public static class ObservedAvatars
{
    /// <summary>Fills <paramref name="avatars"/> with the room's avatars, ordered by client ID.</summary>
    public static void Collect(RealtimeAvatarManager manager, List<RealtimeAvatar> avatars, List<int> clientIds)
    {
        avatars.Clear();
        clientIds.Clear();
        if (manager == null || manager.avatars == null)
            return;

        foreach (KeyValuePair<int, RealtimeAvatar> pair in manager.avatars)
        {
            if (pair.Value != null)
                clientIds.Add(pair.Key);
        }
        clientIds.Sort();
        foreach (int clientId in clientIds)
            avatars.Add(manager.avatars[clientId]);
    }

    /// <summary>
    /// Normcore's AudioOutput plays remote voices as 3D sound. Observers sit far from the users,
    /// so play everything under the avatars as 2D audio instead.
    /// </summary>
    public static void FlattenVoices(List<RealtimeAvatar> avatars)
    {
        foreach (RealtimeAvatar avatar in avatars)
        {
            foreach (AudioSource source in avatar.GetComponentsInChildren<AudioSource>(true))
            {
                source.spatialBlend = 0f;
                source.spatialize = false;
            }
        }
    }

    public static bool IsSpeaking(RealtimeAvatar avatar, float threshold)
    {
        RealtimeAvatarVoice voice = avatar.head != null ? avatar.head.GetComponent<RealtimeAvatarVoice>() : null;
        return voice != null && voice.voiceVolume > threshold;
    }
}
