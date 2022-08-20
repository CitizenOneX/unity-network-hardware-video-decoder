using UnityEngine;
 
public class FPSDisplay : MonoBehaviour
{
	float deltaTime = 0.0f;
	public VideoDepthAudioRenderer v;
	int videoFramesTotal, videoFramesAtSecondStart;
	float currentSecondStart;
	public int vfps, afps;

    private void Start()
    {
		currentSecondStart = Time.realtimeSinceStartup;
    }

    void Update()
	{
		deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;

		videoFramesTotal = v.VideoFrameNumber;

		float currentTime = Time.time;
		if (currentTime - 1.0f > currentSecondStart)
        {
			currentSecondStart = currentTime;
			vfps = videoFramesTotal - videoFramesAtSecondStart;
			videoFramesAtSecondStart = videoFramesTotal;
			Debug.Log($"fps: {1.0f / deltaTime}, vfps: {vfps}");
        }
	}
 /*
	void OnGUI()
	{
		int w = Screen.width, h = Screen.height;
 
		GUIStyle style = new GUIStyle();
 
		Rect rect = new Rect(0, 0, w, 3 * h * 2 / 100);
		style.alignment = TextAnchor.UpperLeft;
		style.fontSize = h * 2 / 100;
		style.normal.textColor = new Color (0.5f, 0.5f, 0.9f, 1.0f);
		float msec = deltaTime * 1000.0f;
		float fps = 1.0f / deltaTime;
		string text = string.Format("{0:0.0} ms ({1:0.} fps)\nvideo: {2:0.} fps\naudio: {3:0.} frames/s", msec, fps, vfps, afps);
		GUI.Label(rect, text, style);
	}*/
}