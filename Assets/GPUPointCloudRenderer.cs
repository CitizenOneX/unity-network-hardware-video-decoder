﻿/*
 * Unity Network Hardware Video Decoder
 * 
 * Copyright 2020 (C) Bartosz Meglicki <meglickib@gmail.com>
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 */

using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

public class GPUPointCloudRenderer : MonoBehaviour
{
	public string device = "/dev/dri/renderD128";
	public string ip = "";
	public ushort port = 9768;

	public ComputeShader unprojectionShader;
	public Shader pointCloudShader;

	private IntPtr unhvd;
	private UNHVD.unhvd_frame frame = new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] };

	private ComputeBuffer depthBuffer;
	private ComputeBuffer vertexBuffer;
	private ComputeBuffer countBuffer;
	
	private Material material;

	void Awake()
	{
		UNHVD.unhvd_net_config net_config = new UNHVD.unhvd_net_config{ip=this.ip, port=this.port, timeout_ms=500 };
		UNHVD.unhvd_hw_config hw_config = new UNHVD.unhvd_hw_config{hardware="vaapi", codec="hevc", device=this.device, pixel_format="p010le", width=848, height=480, profile=2};
		
		Debug.Log("Supports R16" + SystemInfo.SupportsTextureFormat(TextureFormat.R16));

		unhvd = UNHVD.unhvd_init (ref net_config, ref hw_config);
	
		if (unhvd == IntPtr.Zero)
		{
			Debug.Log ("failed to initialize UNHVD");
			gameObject.SetActive (false);
			return;
		}

		depthBuffer = new ComputeBuffer(848*480/2, 4); //stride * height but we have 2 uint16_t in every element (4 bytes)
		//vertexBuffer = new ComputeBuffer(848*480, sizeof(float)*3);
		vertexBuffer = new ComputeBuffer(848*480, sizeof(float)*3, ComputeBufferType.Append);
		countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);

		Vector3[] positions = new Vector3[848*480];
		for(int i=0;i<positions.Length;++i)
			positions[i] = new Vector3(0, 0, 0);//i/100.0f, i/100.0f, i/100.0f);

		uint[] depths = new uint[848/2*480];
		for(int i=0;i<depths.Length;++i)
			depths[i] = 0xFFC0FFC0;

		depthBuffer.SetData(depths);
		vertexBuffer.SetData(positions);

  		unprojectionShader.SetBuffer(0, "depth", depthBuffer);
		unprojectionShader.SetBuffer(0, "vertices", vertexBuffer);

		unprojectionShader.Dispatch(0, 848/2, 480/8, 1);

		Debug.Log("vertices count" + vertexBuffer.count);
		Debug.Log("vertices count from buffer" + getVertexCount());
	}

	private int getVertexCount()
	{
	    ComputeBuffer.CopyCount(vertexBuffer, countBuffer, 0);

    	int[] counter = new int[1] { 0 };
    	countBuffer.GetData(counter);
    	return counter[0];
	}

	void OnDestroy()
	{
		UNHVD.unhvd_close (unhvd);
		if(depthBuffer != null)
			depthBuffer.Release();

		if(vertexBuffer != null)
			vertexBuffer.Release();

		if(countBuffer != null)
			countBuffer.Release();

		if (material != null)
		{
			if (Application.isPlaying)			
				Destroy(material);
			else			
				DestroyImmediate(material);
		}	
	}

	void LateUpdate ()
	{
		//Graphics.DrawProceduralNow(MeshTopology.Points, 848*480, 1);
		bool updateNeeded = false;

		if (UNHVD.unhvd_get_frame_begin(unhvd, ref frame) == 0)
		{
			unsafe
			{
				NativeArray<uint> depth = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<uint>(frame.data[0].ToPointer(), frame.linesize[0] * frame.height / 4, Allocator.None);

				#if ENABLE_UNITY_COLLECTIONS_CHECKS
				NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref depth, AtomicSafetyHandle.Create());
				#endif

				depthBuffer.SetData(depth);
				updateNeeded = true;
			}
		}

		if (UNHVD.unhvd_get_frame_end (unhvd) != 0)
		{
			Debug.LogWarning ("Failed to get UNHVD frame data");
			return;
		}

		if(!updateNeeded)
			return;

		vertexBuffer.SetCounterValue(0);
		unprojectionShader.Dispatch(0, 848/2, 480, 1);
		Debug.Log("vertices count from buffer" + getVertexCount());
	}
 

	void OnRenderObject()
	{
		// Lazy initialization
		if (material == null)
		{
			material = new Material(pointCloudShader);
			material.hideFlags = HideFlags.DontSave;
			material.SetBuffer("vertices", vertexBuffer);
		}

		material.SetPass(0);

		//Graphics.DrawProceduralNow(MeshTopology.Points, 848*480, 1);
		Graphics.DrawProceduralNow(MeshTopology.Points, getVertexCount(), 1);
	}

}
