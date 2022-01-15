/*
 * Unity Network Hardware Video Decoder
 * 
 * Copyright 2019 (C) Bartosz Meglicki <meglickib@gmail.com>
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 */

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public class VideoRenderer : MonoBehaviour
{
	private string hardware = "";
	private string codec = "hevc";
	private string device = "";
	private string pixel_format = "yuv420p"; // should match the texture format
	private TextureFormat texture_format = TextureFormat.R8; // should match the pixel format
	private string ip = "";
	private ushort port = 9766;

	private IntPtr unhvd;
	private UNHVD.unhvd_frame frame = new UNHVD.unhvd_frame { data = new System.IntPtr[3], linesize = new int[3] };
	private Texture2D videoTexture;

	void Awake()
	{
		UNHVD.unhvd_hw_config hw_config = new UNHVD.unhvd_hw_config { hardware = this.hardware, codec = this.codec, device = this.device, pixel_format = this.pixel_format, width = 0, height = 0, profile = 0 };
		UNHVD.unhvd_net_config net_config = new UNHVD.unhvd_net_config { ip = this.ip, port = this.port, timeout_ms = 500 };

		Debug.Log(string.Format("hwconfig: {0}, {1}, {2}, {3}", hw_config.hardware, hw_config.codec, hw_config.device, hw_config.pixel_format));
		Debug.Log(string.Format("netconfig: {0}, {1}", net_config.ip, net_config.port.ToString()));
		Debug.Log("About to perform unhvd_init()");
		unhvd = UNHVD.unhvd_init(ref net_config, ref hw_config);
		Debug.Log("unhvd_init() completed");

		if (unhvd == IntPtr.Zero)
		{
			Debug.Log("failed to initialize UNHVD");
			gameObject.SetActive(false);
		}

		//flip the texture mapping upside down
		Vector2[] uv = GetComponent<MeshFilter>().mesh.uv;
		for (int i = 0; i < uv.Length; ++i)
			uv[i][1] = -uv[i][1];
		GetComponent<MeshFilter>().mesh.uv = uv;
	}
	void OnDestroy()
	{
		UNHVD.unhvd_close(unhvd);
	}

	private void AdaptTexture()
	{
		if (videoTexture == null || videoTexture.width != frame.width || videoTexture.height != frame.height)
		{
			Debug.Log(string.Format("AdaptTexture creating new Texture for Frame: {0}x{1}x{2}, planes:{3},{4},{5}", frame.width, frame.height, frame.format, frame.linesize[0], frame.linesize[1], frame.linesize[2]));
			videoTexture = new Texture2D(frame.width, frame.height, texture_format, false);
			GetComponent<Renderer>().material.mainTexture = videoTexture;
		}
	}

	// Update is called once per frame
	void LateUpdate()
	{
		if (UNHVD.unhvd_get_frame_begin(unhvd, ref frame) == 0)
		{
			AdaptTexture();
			var data = videoTexture.GetRawTextureData<byte>();
			int pixels = frame.width * frame.height;
			unsafe
			{
				//Debug.Log(string.Format("Linesizes:{0}, {1}, {2}", frame.linesize[0], frame.linesize[1], frame.linesize[2]));
				if (false) // quick hack to use manual Y->RGBA decoding
                {
					for (int index = 0; index < pixels; index++)
					{
						// YUV420P has Y, U and V planes
						// just load the w x h luma first into RGB
						byte Y = ((byte*)frame.data[0])[index];
						data[4 * index] = Y;
						data[4 * index + 1] = Y;
						data[4 * index + 2] = Y;
						data[4 * index + 3] = 255;
					}
				}
				else if (false)
                {
					// quadruple the pixels
					for (int index = 0; index < pixels; index++)
					{
						// YUV420P has Y, U and V planes
						// just load the w x h luma first into 4 R8 pixels
						byte Y = ((byte*)frame.data[0])[index];
						data[4 * index] = Y;
						data[4 * index + 1] = Y;
						data[4 * index + 2] = Y;
						data[4 * index + 3] = Y;
					}
				}
				else if (false)
				{
					// quadruple the pixels
					for (int index = 0; index < pixels; index++)
					{
						// YUV420P has Y, U and V planes
						// just load the w x h luma first into R8 pixels
						byte Y = ((byte*)frame.data[0])[index];
						data[index] = Y;
					}
				}
				else
				{
					// IR frames and Y plane of YUV frames should be able to be copied to an R8 texture in one go
                    NativeArray<byte> yplane = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(frame.data[0].ToPointer(), pixels, Allocator.None);
					#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref yplane, AtomicSafetyHandle.Create());
					#endif
					//Debug.Log(string.Format("texture length: {0}, yplane length: {1}", data.Length, yplane.Length));
					// TODO see if I can assign directly rather than copy, but for now it's fine
					//data.CopyFrom(yplane);
					videoTexture.LoadRawTextureData<byte>(yplane);

                }
            }
			videoTexture.Apply(false);
		}

		if (UNHVD.unhvd_get_frame_end(unhvd) != 0)
			Debug.LogWarning("Failed to get UNHVD frame data");
	}
}
