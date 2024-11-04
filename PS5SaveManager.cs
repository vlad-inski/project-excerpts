#if UNITY_PS5

#define PS5_LOG

using System;
using System.IO;
using System.Collections;

using UnityEngine;

using Unity.SaveData.PS5;
using Unity.SaveData.PS5.Core;
using Unity.SaveData.PS5.Info;
using Unity.SaveData.PS5.Mount;
using Unity.SaveData.PS5.Initialization;
using Unity.SaveData.PS5.Search;
using Unity.SaveData.PS5.Dialog;
using Unity.SaveData.PS5.Delete;
using Unity.SaveData.PS5.Backup;



public class PS5SaveManager
{
	private readonly MonoBehaviour m_parentMonobehaviour  = null;
	private InitResult m_saveDataInitResult = new InitResult();
	private int m_ps5UserId = SCE_USER_SERVICE_USER_ID_INVALID;
	private UInt64 m_u64SaveDataSizeInBlocks = 0;
	private Dialogs.NewItem m_ps5DialogNewItem = null;                   
	private SaveDataParams  m_ps5SaveDataParams = default(SaveDataParams);
	private static Mounting.MountResponse m_ps5MountPointResponse = null;
	private static uint SCE_SAVE_DATA_DIRNAME_MAX_COUNT = 10;
	private const int SCE_USER_SERVICE_USER_ID_INVALID = -1;

	public bool IsInitialised => m_saveDataInitResult.Initialized;
	public int OwningPS5UserId => m_ps5UserId;
	public bool IsInitialisedforUser => SCE_USER_SERVICE_USER_ID_INVALID != m_ps5UserId;
	
	public static UInt64 GetSaveDataBlocksRequiredForMaxSaveFileOfSizeInBytes( int maxBytesInSave )
	{
		const float RecommendedAdditionalSizeMultiplier = 1.1f;

		Log( $"{nameof(maxBytesInSave)}: {maxBytesInSave}" );

		int maxBytesInSaveAdjusted = Mathf.FloorToInt( maxBytesInSave * RecommendedAdditionalSizeMultiplier );

		Log( $"{nameof(maxBytesInSaveAdjusted)}: {maxBytesInSaveAdjusted}" );

		int maxSaveRoundedUpToNextBlockSizeBytes = RoundNumberUpToPowerOf2Boundary( maxBytesInSaveAdjusted, Mounting.MountRequest.BLOCK_SIZE );

		Log( $"{nameof(maxSaveRoundedUpToNextBlockSizeBytes)}: {maxSaveRoundedUpToNextBlockSizeBytes}" );

		int numBlocksForSave = ( Mounting.MountRequest.BLOCKS_MIN + ( maxSaveRoundedUpToNextBlockSizeBytes / Mounting.MountRequest.BLOCK_SIZE ) );

		Log( $"{nameof(numBlocksForSave)}: {numBlocksForSave}" );

		return(UInt64) numBlocksForSave;
	}

	private static int RoundNumberUpToPowerOf2Boundary( int numberToRound, int powerOf2ToRoundUpTo )
	{
		if( ( powerOf2ToRoundUpTo & ( powerOf2ToRoundUpTo - 1 ) ) != 0 )
		{
			Debug.LogError( $"value passed as iPowerOf2ToRoundUpTo [{powerOf2ToRoundUpTo}] is NOT a power of 2" );
			return numberToRound;
		}

		int iAboveAlignmentBoundary  = numberToRound + ( powerOf2ToRoundUpTo - 1 );
		int iBitFieldMaskToRoundDown = ~( powerOf2ToRoundUpTo                 - 1 );
		int iAlignedValueToReturn    = ( iAboveAlignmentBoundary & iBitFieldMaskToRoundDown );

		return iAlignedValueToReturn;
	}
	
	public PS5SaveManager( MonoBehaviour parentMonobehaviour )
	{
		m_parentMonobehaviour = parentMonobehaviour;
	}
	
	public void PlatformInitialise()
	{
		try
		{
			InitSettings settings = new InitSettings();
			settings.Affinity = ThreadAffinity.Core5;
			m_saveDataInitResult = Main.Initialize( settings );

			if( m_saveDataInitResult.Initialized )
			{
				Log( "Initialised save data ok" );

				Main.OnAsyncEvent += Main_OnAsyncEvent;
			}
			else
			{
				Log( "<color=\"red\">FAILED TO INITIALISE save data</color>" );
			}
		}
		catch( SaveDataException e )
		{
			Log( "Exception During Initialization : " + e.ExtendedMessage );
		}
	}
	
	public void PlatformTerminate()
	{
		Main.OnAsyncEvent -= Main_OnAsyncEvent;

		try
		{
			Main.Terminate();

			m_saveDataInitResult = new InitResult();
		}
		catch( SaveDataException e )
		{
			Log( $"<color=\"red\">Exception During Termination : " + e.ExtendedMessage );
		}
	}

	
	public void InitialiseGameSpecificParams( int maxSaveFileSizeInBytes, string iconPathInStreamingAssets, string saveTitle )
	{
		m_u64SaveDataSizeInBlocks = GetSaveDataBlocksRequiredForMaxSaveFileOfSizeInBytes( maxSaveFileSizeInBytes );

		m_ps5DialogNewItem = new Dialogs.NewItem()
		{
			IconPath = $"/app0/Media/StreamingAssets/{iconPathInStreamingAssets}",
			Title    = saveTitle,
		};
	}

	public void InitialiseSaveDataParams( string saveTitle, string saveSubTitle, string saveDetail )
	{
		m_ps5DialogNewItem.Title = saveTitle;

		m_ps5SaveDataParams = new SaveDataParams()
		{
			Title    = saveTitle,
			SubTitle = saveSubTitle,
			Detail   = saveDetail,
		};
	}


	public void InitialiseForUser( int ps5UserId, Action< int > funcOnCompleted)
	{
		IEnumerator InitialiseForUser( int userId, Action< int > onCompleted )
		{
			Log( $"Initialising for user Id {userId}:/[{m_u64SaveDataSizeInBlocks} blocks]" );

			yield return null;
			Log( $"...initiailised for user Id {userId}" );
			onCompleted.Invoke( PS5ErrorCode.SCE_OK );
		}
		
		m_ps5UserId  = ps5UserId;

		m_parentMonobehaviour.StartCoroutine( InitialiseForUser( m_ps5UserId, funcOnCompleted ) );
	}
	

	public void RequestCheckSystemFreeBlocks( int maxRequiredSaveSize, Action< int, bool > onCheckSpaceOperationFinished )
	{
		IEnumerator CheckFreeSpace()
		{
			yield return null;
			onCheckSpaceOperationFinished.Invoke( PS5ErrorCode.SCE_OK, true );
		}

		m_parentMonobehaviour.StartCoroutine( CheckFreeSpace() );
	}
	
	public void RequestCheckSaveExists(DirName dirName, string fileName, Action< int, bool > onCheckExistsOperationFinished )
	{
		Log( $"[GameSaveManager.RequestCheckSaveExists] - checking for data for user {m_ps5UserId} save:{dirName.Data}/{fileName}" );
		m_parentMonobehaviour.StartCoroutine( CheckSaveExists( m_ps5UserId, m_u64SaveDataSizeInBlocks, dirName, fileName, onCheckExistsOperationFinished ) );
	}
	
	public void RequestSave( byte[] dataToSave, DirName dirName, string fileName, bool withBackUp, Action< int > onSaveOperationFinished )
	{
		Log( $"[GameSaveManager.RequestSaveForUser] - saving for user {m_ps5UserId}" );
		m_parentMonobehaviour.StartCoroutine( SaveSaveData( m_ps5UserId, m_u64SaveDataSizeInBlocks, dirName, m_ps5DialogNewItem, m_ps5SaveDataParams, fileName, withBackUp, dataToSave, onSaveOperationFinished ) );
	}
	
	public void RequestLoad(DirName dirName, string fileName,  Action< int, byte[] > onLoadOperationFinished )
	{
		Log( $"[GameSaveManager.RequestLoadForUser] - saving for user {m_ps5UserId}" );
		m_parentMonobehaviour.StartCoroutine( LoadSaveData( m_ps5UserId, m_u64SaveDataSizeInBlocks, dirName, fileName, onLoadOperationFinished ) );
	}
	
	public void RequestDelete(DirName dirName, Action<int> onDeleteOperationFinished)
	{
		Log( $"[GameSaveManager.RequestSaveForUser] - deleting for user {m_ps5UserId}" );
		m_parentMonobehaviour.StartCoroutine( DeleteSaveData( m_ps5UserId, dirName, onDeleteOperationFinished ) );
	}
	
	public void DeleteAllForUser()
	{
		Log( $"[GameSaveManager.RequestSaveForUser] - deleting all for user {m_ps5UserId}" );
		m_parentMonobehaviour.StartCoroutine( DeleteAllSaveData( m_ps5UserId ) );
	}
	
	public void RequestShowDialogForLoadError( int sceError, Action< int > onDialogClosed )
	{
		Log( $"[GameSaveManager.RequestShowDialogForLoadError] - show error dialog for user {m_ps5UserId}" );
		m_parentMonobehaviour.StartCoroutine( ShowDialogForError( m_ps5UserId, m_ps5DialogNewItem, Dialogs.DialogType.Load, sceError, onDialogClosed ) );
	}
	
	public void RequestShowDialogForSaveError( int sceError, Action< int > onDialogClosed )
	{
		Log( $"[GameSaveManager.RequestShowDialogForLoadError] - show error dialog for user {m_ps5UserId}" );
		m_parentMonobehaviour.StartCoroutine( ShowDialogForError( m_ps5UserId, m_ps5DialogNewItem, Dialogs.DialogType.Save, sceError, onDialogClosed ) );
	}
	
	public void RequestShowDialogNoSpaceToCreateSave( Action< int > onDialogClosed )
	{
		Log( $"[GameSaveManager.RequestShowDialogForNoSpace] - show error dialog for user {m_ps5UserId}" );
		m_parentMonobehaviour.StartCoroutine( HandleNoSpaceToSave( m_ps5UserId, m_ps5DialogNewItem, onDialogClosed ) );
	}
	
	private void Main_OnAsyncEvent( SaveDataCallbackEvent callbackEvent )
	{
		Log(	$"API: {callbackEvent.ApiCalled} - ([User Id:  0x{callbackEvent.UserId.ToString( "X8" )}] - Request Id: {callbackEvent.RequestId} " +
										$"Response: {callbackEvent.Response?.ReturnCodeValue ?? -1 } (type: {callbackEvent.Response?.GetType().Name ?? "none"}" );
	}
	
	private class SaveDataResultToken
	{
		public int  SceResultCode => m_SceResultCode;
		public bool IsAnError => ( !PS5ErrorCode.Succeeded( m_SceResultCode ) );
		public void OverwriteResultCodeIfNotAlreadyError( int newResultCode ) => m_SceResultCode = ( PS5ErrorCode.SCE_OK == m_SceResultCode ) ? newResultCode : m_SceResultCode;

		private int m_SceResultCode = PS5ErrorCode.SCE_OK;
	}
	
	private static int StartSearchRequest( Searching.DirNameSearchResponse response, int ps5UserId )
	{
		var request = new Searching.DirNameSearchRequest()
		{
			UserId           = ps5UserId,
			Key              = Searching.SearchSortKey.Time,
			Order            = Searching.SearchSortOrder.Ascending,
			IncludeBlockInfo = true,
			IncludeParams    = true,
			MaxDirNameCount  = SCE_SAVE_DATA_DIRNAME_MAX_COUNT,
			IgnoreCallback   = true,
		};

		try
		{
			var requestId = Searching.DirNameSearch( request, response );
			Log( $"StartSearchRequest - started request Id: {requestId}" );

		}
		catch( SaveDataException e )
		{
			Log( $"StartSearchRequest - Exception: {e.ExtendedMessage}" );
			return e.SceErrorCode;
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator HandleSearchRequest( SaveDataResultToken resultToken, Searching.DirNameSearchResponse response, int ps5UserId )
	{
		var sceStartSearchResult = StartSearchRequest( response, ps5UserId );

		if( !PS5ErrorCode.Succeeded( sceStartSearchResult ) )
		{
			resultToken.OverwriteResultCodeIfNotAlreadyError( sceStartSearchResult );
			yield break;
		}

		while( response.Locked )
		{
			yield return null;
		}

		Log( "HandleSearchRequest finished" );
		resultToken.OverwriteResultCodeIfNotAlreadyError( response.ReturnCodeValue );
	}


	private static int StartListFilesRequest( ListFilesResponse listFileResponse, int ps5UserId, Mounting.MountPoint mountPoint )
	{
		try
		{
			var request   = new ListFilesRequest( ps5UserId, mountPoint );
			var requestId = FileOps.CustomFileOp( request, listFileResponse );
			Log( $"StartListFilesRequest - started request Id: {requestId}" );
		}
		catch( SaveDataException e )
		{
			Log( $"StartListFilesRequest - Exception: {e.ExtendedMessage}" );

			return e.SceErrorCode;
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator HandleListFilesRequest( SaveDataResultToken resultToken, ListFilesResponse response, int ps5UserId, Mounting.MountPoint mountPoint )
	{
		var startListFilesResult = StartListFilesRequest( response, ps5UserId, mountPoint );

		if( !PS5ErrorCode.Succeeded( startListFilesResult ) )
		{
			resultToken.OverwriteResultCodeIfNotAlreadyError( startListFilesResult );
			yield break;
		}

		while( response.Locked )
		{
			yield return null;
		}

		Log( "HandleListFilesRequest finished" );
		resultToken.OverwriteResultCodeIfNotAlreadyError( response.ReturnCodeValue );
	}
	
	private static int StartMountRequest( Mounting.MountResponse mountResponse, int userId, UInt64 blocks, DirName ps5DirName, Mounting.MountModeFlags flags )
	{
		try
		{
			Mounting.MountRequest request = new Mounting.MountRequest()
			{
				UserId         = userId,
				IgnoreCallback = true,
				MountMode      = flags,
				Blocks         = blocks,
				DirName        = ps5DirName,
			};

			var requestId = Mounting.Mount( request, mountResponse );
			Log( $"StartMountRequest - started request Id: {requestId}" );
		}
		catch( SaveDataException e )
		{
			Log( $"StartMountRequest - Exception: {e.ExtendedMessage}" );

			if( !PS5ErrorCode.Succeeded( mountResponse.ReturnCodeValue ) )
			{
				return mountResponse.ReturnCodeValue;
			}
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator HandleMountRequest( SaveDataResultToken resultToken, Mounting.MountResponse mountResponse, int ps5UserId, UInt64 u64SaveDataSizeInBlocks, DirName ps5DirName, Mounting.MountModeFlags mountFlags )
	{
		var startMountResult = StartMountRequest( mountResponse, ps5UserId, u64SaveDataSizeInBlocks, ps5DirName, mountFlags );

		if( !PS5ErrorCode.Succeeded( startMountResult ) )
		{
			resultToken.OverwriteResultCodeIfNotAlreadyError( startMountResult );

			yield break;
		}

		while( mountResponse.Locked )
		{
			yield return null;
		}

		Log( $"HandleMountRequest finished - {PS5ErrorCode.GetName( mountResponse.ReturnCodeValue )}" );

		resultToken.OverwriteResultCodeIfNotAlreadyError( mountResponse.ReturnCodeValue );
	}
	
	private static int StartUnmountRequest( EmptyResponse unmountResponse, int ps5UserId, Mounting.MountPoint mountPoint )
	{
		try
		{
			Mounting.UnmountRequest request = new Mounting.UnmountRequest()
			{
				UserId         = ps5UserId,
				MountPointName = mountPoint.PathName,
				IgnoreCallback = true,				
			};

			var requestId = Mounting.Unmount( request, unmountResponse );
			Log( $"StartUnmountRequest - started request Id: {requestId}" );
		}
		catch
		{
			if( unmountResponse.ReturnCodeValue < 0 )
			{
				return unmountResponse.ReturnCodeValue;
			}
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator HandleUnmountRequest( SaveDataResultToken resultToken, EmptyResponse unmountResponse, int ps5UserId, Mounting.MountPoint mountPoint )
	{
		var startUnmountResult = StartUnmountRequest( unmountResponse, ps5UserId, mountPoint );

		if( !PS5ErrorCode.Succeeded( startUnmountResult ) )
		{
			resultToken.OverwriteResultCodeIfNotAlreadyError( startUnmountResult );
		}

		while( unmountResponse.Locked )
		{
			yield return null;
		}

		Log( "HandleUnmountRequest finished" );

		resultToken.OverwriteResultCodeIfNotAlreadyError( unmountResponse.ReturnCodeValue );
	}


	private class ListFilesRequest : FileOps.FileOperationRequest
	{
		public ListFilesRequest( int ps5UserId, Mounting.MountPoint mountPoint )
		{
			UserId         = ps5UserId;
			MountPointName = mountPoint.PathName;
			IgnoreCallback = true;
		}

		public override void DoFileOperations( Mounting.MountPoint mountPoint, FileOps.FileOperationResponse response )
		{
			var outpathString = mountPoint.PathName.Data;
			var fileResponse  = response as ListFilesResponse;

			fileResponse.files = Directory.GetFiles( outpathString, "*.*", SearchOption.AllDirectories );
		}
	}

	private class ListFilesResponse : FileOps.FileOperationResponse
	{
		public string[] files;
	}
	
	private static IEnumerator CheckSaveExists( int ps5UserId, UInt64 u64SaveDataSizeInBlocks, DirName ps5DirName, string fileName, Action< int, bool > onCheckExistsOperationFinished )
	{
		var saveDataResultToken = new SaveDataResultToken();
		var foundSaveFile       = false;
		
		var searchResponse = new Searching.DirNameSearchResponse();

		yield return HandleSearchRequest( saveDataResultToken, searchResponse, ps5UserId );

		if( saveDataResultToken.IsAnError )
		{
			onCheckExistsOperationFinished( saveDataResultToken.SceResultCode, false );
			yield break;
		}

		var foundSaveDataDir = false;
		switch( searchResponse.ReturnCode )
		{
			case ReturnCodes.SUCCESS:
			{
				if( searchResponse.SaveDataItems != null )
				{
					foreach (var dir in searchResponse.SaveDataItems)
					{
						if (String.Equals(dir.DirName.Data, ps5DirName.Data))
						{
							foundSaveDataDir = true;
						}
					}
				}
			} 
				break;
		}

		if( !foundSaveDataDir )
		{
			onCheckExistsOperationFinished( saveDataResultToken.SceResultCode, false );
			yield break;
		}
		
		
		var mountResponse = new Mounting.MountResponse();
		{
			yield return HandleMountRequest( saveDataResultToken, mountResponse, ps5UserId, u64SaveDataSizeInBlocks, ps5DirName, Mounting.MountModeFlags.ReadOnly );

			if( saveDataResultToken.IsAnError )
			{
				onCheckExistsOperationFinished( saveDataResultToken.SceResultCode, false );
				yield break;
			}
		}

		var listFilesResponse = new ListFilesResponse();
		yield return HandleListFilesRequest( saveDataResultToken, listFilesResponse, ps5UserId, mountResponse.MountPoint );

		if( saveDataResultToken.IsAnError )
		{
			yield return UnmountSaveData( saveDataResultToken, ps5UserId, mountResponse );
			
			onCheckExistsOperationFinished( saveDataResultToken.SceResultCode, false );
			yield break;
		}

		if (listFilesResponse.files != null)
		{
			foreach (var file in listFilesResponse.files)
			{
				if (String.Equals(Path.GetFileName(file), fileName))
				{
					foundSaveFile = true;
				}
			}
		}

		yield return UnmountSaveData( saveDataResultToken, ps5UserId, mountResponse );
		
		onCheckExistsOperationFinished( saveDataResultToken.SceResultCode, foundSaveFile );
	}


	private class WriteSaveDataResponse : FileOps.FileOperationResponse
	{}

	private class WriteSaveDataRequest : FileOps.FileOperationRequest
	{
		private readonly string SaveDataFileName;
		private readonly byte[] SaveDataBytes;
		
		public WriteSaveDataRequest( int ps5UserId, Mounting.MountPoint mountPoint, string saveDataFileName, byte[] saveData )
		{
			UserId           = ps5UserId;
			MountPointName   = mountPoint.PathName;
			SaveDataFileName = saveDataFileName;
			SaveDataBytes    = saveData;
			IgnoreCallback   = true;
		}
		
		public override void DoFileOperations( Mounting.MountPoint mountPoint, FileOps.FileOperationResponse response )
		{
			var outputFilePath = $"{mountPoint.PathName.Data}/{SaveDataFileName}";
			Log( $"WriteSaveDataRequest: about to write file: {outputFilePath} [{SaveDataBytes.Length} bytes]" );
			
			File.WriteAllBytes( outputFilePath, SaveDataBytes );
		}
	}


	private static int StartWriteIconRequest( EmptyResponse iconResponse, int userId, Mounting.MountPoint mountPoint, Dialogs.NewItem newItem )
	{
		try
		{
			Mounting.SaveIconRequest request = new Mounting.SaveIconRequest()
			{
				UserId         = userId,
				MountPointName = mountPoint.PathName,
				RawPNG         = newItem.RawPNG,
				IconPath       = newItem.IconPath,
				IgnoreCallback = true,
			};

			var requestId = Mounting.SaveIcon( request, iconResponse );
			Log( $"StartWriteIconRequest - started request Id: {requestId}" );
		}
		catch
		{
			if( iconResponse.ReturnCodeValue < 0 )
			{
				return iconResponse.ReturnCodeValue;
			}
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator HandleWriteIconRequest( SaveDataResultToken resultToken, EmptyResponse response, int ps5UserId, Mounting.MountPoint mountPoint, Dialogs.NewItem ps5NewItem )
	{
		var startIconResult = StartWriteIconRequest( response, ps5UserId, mountPoint, ps5NewItem );

		if( !PS5ErrorCode.Succeeded( startIconResult ) )
		{
			resultToken.OverwriteResultCodeIfNotAlreadyError( startIconResult ); 
			yield break;
		}

		while( response.Locked )
		{
			yield return null;
		}

		Log( $"HandleWriteIconRequest finished - {PS5ErrorCode.GetName(response.ReturnCodeValue)}" );
		resultToken.OverwriteResultCodeIfNotAlreadyError( response.ReturnCodeValue ); 
	}

	private static int StartWriteParamsRequest( EmptyResponse response, int ps5UserId, Mounting.MountPoint mountPoint, SaveDataParams saveDataParams )
	{
		try
		{
			Mounting.SetMountParamsRequest request = new Mounting.SetMountParamsRequest()
			{
				UserId         = ps5UserId,
				MountPointName = mountPoint.PathName,
				IgnoreCallback = true,
				Params         = saveDataParams,
			};

			var requestId = Mounting.SetMountParams( request, response );
			Log( $"StartWriteParamsRequest - started request Id: {requestId}" );
		}
		catch
		{
			if( response.ReturnCodeValue < 0 )
			{
				return response.ReturnCodeValue;
			}
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator HandleWriteParamsRequest( SaveDataResultToken resultToken, EmptyResponse response, int ps5UserId, Mounting.MountPoint mountPoint, SaveDataParams saveDataParams )
	{
		var startParamsResult = StartWriteParamsRequest( response, ps5UserId, mountPoint, saveDataParams );

		if( !PS5ErrorCode.Succeeded( startParamsResult ) )
		{
			resultToken.OverwriteResultCodeIfNotAlreadyError( startParamsResult ); 
			yield break;
		}

		while( response.Locked )
		{
			yield return null;
		}

		Log( $"HandleWriteParamsRequest finished - {PS5ErrorCode.GetName(response.ReturnCodeValue)}" );
		resultToken.OverwriteResultCodeIfNotAlreadyError( response.ReturnCodeValue ); 
	}

	private static int StartWriteSaveDataRequest( WriteSaveDataResponse response, int ps5UserId, Mounting.MountPoint mountPoint, string saveDataFileName, byte[] saveDataBytes )
	{
		try
		{
			var request   = new WriteSaveDataRequest( ps5UserId, mountPoint, saveDataFileName, saveDataBytes );
			var requestId = FileOps.CustomFileOp( request, response );
			Log( $"StartWriteSaveDataRequest - started request Id: {requestId}" );
		}
		catch( SaveDataException e )
		{
			Log( $"StartWriteSaveDataRequest - Exception: {e.ExtendedMessage}" );
			return e.SceErrorCode;
		}

		return PS5ErrorCode.SCE_OK;
	}


	private static IEnumerator HandleWriteSaveDataRequest( SaveDataResultToken resultToken, WriteSaveDataResponse response, int ps5UserId, Mounting.MountPoint mountPoint, string saveDataFileName, byte[] saveDataBytes )
	{
		var startWriteResult = StartWriteSaveDataRequest( response, ps5UserId, mountPoint, saveDataFileName, saveDataBytes );
		
		if( !PS5ErrorCode.Succeeded( startWriteResult ) )
		{
			resultToken.OverwriteResultCodeIfNotAlreadyError( startWriteResult ); 
			yield break;
		}

		while( response.Locked )
		{
			yield return null;
		}

		Log( $"HandleWriteSaveDataRequest finished - {PS5ErrorCode.GetName(response.ReturnCodeValue)}" );
		resultToken.OverwriteResultCodeIfNotAlreadyError( response.ReturnCodeValue ); 
	}

	private static int StartBackupSaveDataRequest( EmptyResponse response, int ps5UserId, DirName ps5DirName )
	{
		try
		{
			Backups.BackupRequest request = new Backups.BackupRequest()
			{
				UserId         = ps5UserId,
				DirName        = ps5DirName,
				IgnoreCallback = true,
			};

			int requestId = Backups.Backup(request, response);			
			Log( $"StartBackupSaveDataRequest - started request Id: {requestId}" );	
		}
		catch( SaveDataException e )
		{
			Log( $"StartBackupSaveDataRequest - Exception: {e.ExtendedMessage}" );
			return e.SceErrorCode;
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator HandleBackupSaveDataRequest( SaveDataResultToken resultToken, EmptyResponse response, int ps5UserId, DirName ps5DirName )
	{
		#region local function & data fro interaction with save data event callback

		bool otherThread_waitingForBackupNotification = false;

		bool WeAreWaitingForBackupToComplete() => otherThread_waitingForBackupNotification;

		void StartWaitForBackupCompleteCallback()
		{
			Main.OnAsyncEvent                        += OnAsyncEventCheckForBackupCompleted;
			otherThread_waitingForBackupNotification =  true;
		}

		void EndWaitForBackupCompleteCallback()
		{
			Main.OnAsyncEvent                        -= OnAsyncEventCheckForBackupCompleted;
			otherThread_waitingForBackupNotification =  false;
		}

		void OnAsyncEventCheckForBackupCompleted( SaveDataCallbackEvent callbackEvent )
		{
			Log( $" OnAsyncEventCheckForBackupCompleted - event: {callbackEvent.ApiCalled}" );

			switch( callbackEvent.ApiCalled )
			{
			case FunctionTypes.NotificationBackup:
				{
					EndWaitForBackupCompleteCallback();
					var backupNotificationResponse = callbackEvent.Response as BackupNotification;
					resultToken.OverwriteResultCodeIfNotAlreadyError( backupNotificationResponse?.ReturnCodeValue ?? PS5ErrorCode.PL_SAVEDATA_IO_BACKUP_NO_RESPONSE );
				}
				break;
			}
		}

		#endregion local function & data fro interaction with save data event callback

		var starBackupResult = StartBackupSaveDataRequest( response, ps5UserId, ps5DirName );
		
		if( !PS5ErrorCode.Succeeded( starBackupResult ) )
		{
			resultToken.OverwriteResultCodeIfNotAlreadyError( starBackupResult ); 
			yield break;
		}

		StartWaitForBackupCompleteCallback();
		
		while( response.Locked )
		{
			yield return null;
		}

		if( response.IsErrorCode )
		{
			EndWaitForBackupCompleteCallback();
			resultToken.OverwriteResultCodeIfNotAlreadyError( response.ReturnCodeValue );
			yield break;
		}

		while( WeAreWaitingForBackupToComplete() )
		{
			yield return null;
		}
		
		Log( $"HandleBackupSaveDataRequest finished - {PS5ErrorCode.GetName( response.ReturnCodeValue )}" );
	}

	private static IEnumerator SaveSaveData( int ps5UserId, UInt64 u64SaveDataSizeInBlocks, DirName ps5DirName, Dialogs.NewItem ps5NewItem, SaveDataParams ps5SaveDataParams, string fileName, bool backUpAfterUnMount, byte[] dataToSave, Action< int > onSaveOperationFinished )
	{
		var	saveDataResultToken = new SaveDataResultToken();
		var mountResponse = new Mounting.MountResponse();
		
		var mountReadWriteCreateIfNecessary = ( Mounting.MountModeFlags.Create2 | Mounting.MountModeFlags.ReadWrite );
		yield return HandleMountRequest( saveDataResultToken, mountResponse, ps5UserId, u64SaveDataSizeInBlocks, ps5DirName, mountReadWriteCreateIfNecessary );										

		if( saveDataResultToken.IsAnError )
		{
			onSaveOperationFinished( saveDataResultToken.SceResultCode );
			yield break;
		}
		

		var saveIconResponse = new EmptyResponse();
		yield return HandleWriteIconRequest( saveDataResultToken, saveIconResponse, ps5UserId, mountResponse.MountPoint, ps5NewItem );

		if( saveDataResultToken.IsAnError )
		{
			yield return UnmountSaveData( saveDataResultToken, ps5UserId, mountResponse );
			
			onSaveOperationFinished( saveDataResultToken.SceResultCode );	
			yield break;
		}


		var saveParamsResponse = new EmptyResponse();
		yield return HandleWriteParamsRequest( saveDataResultToken, saveParamsResponse, ps5UserId, mountResponse.MountPoint, ps5SaveDataParams );

		if( saveDataResultToken.IsAnError )
		{
			yield return UnmountSaveData( saveDataResultToken, ps5UserId, mountResponse );
			
			onSaveOperationFinished( saveDataResultToken.SceResultCode );	
			yield break;
		}

		
		var saveDataResponse = new WriteSaveDataResponse();
		yield return HandleWriteSaveDataRequest( saveDataResultToken, saveDataResponse, ps5UserId, mountResponse.MountPoint, fileName, dataToSave );  
		
		if( saveDataResponse.Exception != null )
		{
			Log( $"Exception during file IO: [0x{saveDataResponse.Exception.HResult:x8}] {saveDataResponse.Exception.Message}" );

			var plSceErrorCode = PS5ErrorCode.MapFileIOExceptionToError( saveDataResponse.Exception ); 
			Log( $"--> pl error [ox{plSceErrorCode:x8} - {PS5ErrorCode.GetName(plSceErrorCode)}" );
			
			saveDataResultToken.OverwriteResultCodeIfNotAlreadyError( plSceErrorCode );																		
		}
		
		yield return UnmountSaveData( saveDataResultToken, ps5UserId, mountResponse );

		if(	backUpAfterUnMount && !saveDataResultToken.IsAnError )
		{
			var backupResponse = new EmptyResponse();
			yield return HandleBackupSaveDataRequest( saveDataResultToken, backupResponse, ps5UserId, ps5DirName );
		}
		
		onSaveOperationFinished( saveDataResultToken.SceResultCode );	
	}

	private static IEnumerator UnmountSaveData(SaveDataResultToken saveDataResultToken, int ps5UserId,
		Mounting.MountResponse mountResponse)
	{
		Debug.Assert( !mountResponse.IsErrorCode, PS5ErrorCode.GetName(mountResponse.ReturnCodeValue) );
		Debug.Assert( mountResponse.MountPoint != null, PS5ErrorCode.GetName(mountResponse.ReturnCodeValue) );
		
		var unmountResponse = new EmptyResponse();
		yield return HandleUnmountRequest( saveDataResultToken, unmountResponse, ps5UserId, mountResponse.MountPoint );
	}


	private class ReadSaveDataResponse : FileOps.FileOperationResponse
	{
		public byte[] SaveDataBytes;
	}
	
	public class ReadSaveDataRequest : FileOps.FileOperationRequest
	{
		public readonly string SaveDataFileName; 
		
		public ReadSaveDataRequest( int ps5UserId, Mounting.MountPoint mountPoint, string saveDataFileName )
		{
			UserId           = ps5UserId;
			MountPointName   = mountPoint.PathName;
			SaveDataFileName = saveDataFileName;
			IgnoreCallback   = true;
		}

		public override void DoFileOperations( Mounting.MountPoint mountPoint, FileOps.FileOperationResponse response )
		{
			if (response is ReadSaveDataResponse readFileResponse)
			{
				readFileResponse.SaveDataBytes = null;

				var saveFilePath = $"{mountPoint.PathName.Data}/{SaveDataFileName}";
				Log($"about to read file: {saveFilePath}");

				using (var fileStream = File.Open(saveFilePath, FileMode.Open, FileAccess.Read))
				{
					var numBytesInSave = (int) fileStream.Length;
					Log($"opened file: {saveFilePath} - contains {numBytesInSave} bytes");

					var byteArrayToReadInto = new byte[numBytesInSave];
					var numBytesRead = fileStream.Read(byteArrayToReadInto, 0, numBytesInSave);

					Log($"Save loaded. Size: {numBytesInSave} (loaded: {numBytesRead}");

					readFileResponse.SaveDataBytes = byteArrayToReadInto;
				}
			}
		}
	}

	private static int StartReadSaveDataRequest( ReadSaveDataResponse response, int ps5UserId, Mounting.MountPoint mountPoint, string saveDataFileName )
	{
		try
		{
			var request   = new ReadSaveDataRequest( ps5UserId, mountPoint, saveDataFileName );
			var requestId = FileOps.CustomFileOp( request, response );
			Log( $"StartReadSaveDataRequest - started request Id: {requestId}" );
		}
		catch( SaveDataException e )
		{
			Log( $"StartReadSaveDataRequest - Exception: {e.ExtendedMessage}" );
			return e.SceErrorCode;
		}

		return PS5ErrorCode.SCE_OK;
	}


	private static IEnumerator HandleReadSaveDataRequest( SaveDataResultToken resultToken, ReadSaveDataResponse response, int ps5UserId, Mounting.MountPoint mountPoint, string saveDataFileName )
	{
		var startReadResult = StartReadSaveDataRequest( response, ps5UserId, mountPoint, saveDataFileName );
		
		if( !PS5ErrorCode.Succeeded( startReadResult ) )
		{
			resultToken.OverwriteResultCodeIfNotAlreadyError( startReadResult ); 
			yield break;
		}

		while( response.Locked )
		{
			yield return null;
		}

		Log( $"HandleReadSaveDataRequest finished - {PS5ErrorCode.GetName(response.ReturnCodeValue)}" );
		resultToken.OverwriteResultCodeIfNotAlreadyError( response.ReturnCodeValue ); 
	}

	private static IEnumerator LoadSaveData( int ps5UserId, UInt64 u64SaveDataSizeInBlocks, DirName ps5DirName, string fileName, Action< int, byte[] > onLoadOperationFinished )
	{
		var saveDataResultToken   = new SaveDataResultToken();
		var bytesReadFromSaveData = (byte[]) null;
		
		var mountResponse = new Mounting.MountResponse();
		var mountReadOnly = Mounting.MountModeFlags.ReadOnly;
		yield return HandleMountRequest( saveDataResultToken, mountResponse, ps5UserId, u64SaveDataSizeInBlocks, ps5DirName, mountReadOnly );										

		if( saveDataResultToken.IsAnError )
		{
			onLoadOperationFinished( saveDataResultToken.SceResultCode, null );
			yield break;
		}

		var readSaveDataResponse = new ReadSaveDataResponse();
		yield return HandleReadSaveDataRequest( saveDataResultToken, readSaveDataResponse, ps5UserId, mountResponse.MountPoint, fileName );
		
		if( readSaveDataResponse.Exception != null)
		{
			Log( $"Exception during file IO: [0x{readSaveDataResponse.Exception.HResult:x8}] {readSaveDataResponse.Exception.Message}" );

			var plSceErrorCode = PS5ErrorCode.MapFileIOExceptionToError( readSaveDataResponse.Exception ); 
			Log( $"--> pl error [0x{plSceErrorCode:x8} - {PS5ErrorCode.GetName(plSceErrorCode)}" );
			
			saveDataResultToken.OverwriteResultCodeIfNotAlreadyError( plSceErrorCode );																		
		}
		
		bytesReadFromSaveData = readSaveDataResponse.SaveDataBytes;
		
		yield return UnmountSaveData( saveDataResultToken, ps5UserId, mountResponse );
		
		onLoadOperationFinished( saveDataResultToken.SceResultCode, bytesReadFromSaveData );		
	}


	private static int StartDeleteDataRequest( EmptyResponse response, int ps5UserId, DirName ps5DirName )
	{
		try
		{
			var request = new Deleting.DeleteRequest()
			{
				UserId         = ps5UserId,
				DirName        = ps5DirName,
				IgnoreCallback = true,
			};

			var requestId = Deleting.Delete( request, response );
			Log( $"StartDeleteDataRequest - started request Id: {requestId}" );
		}
		catch( SaveDataException e )
		{
			Log( $"StartDeleteDataRequest - Exception: {e.ExtendedMessage}" );
			return e.SceErrorCode;
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator DeleteSaveData( int ps5UserId, DirName ps5DirName, Action<int> onDeleteOperationFinished )
	{
		var deleteResponse = new EmptyResponse();
		var startDeleteResult= StartDeleteDataRequest( deleteResponse, ps5UserId, ps5DirName );

		if( !PS5ErrorCode.Succeeded( startDeleteResult ) )
		{
			yield break;
		}

		while( deleteResponse.Locked )
		{
			yield return null;
		}
		
		onDeleteOperationFinished.Invoke( deleteResponse.ReturnCodeValue );
	}
	
	private static IEnumerator DeleteAllSaveData( int ps5UserId )
	{
		var saveDataResultToken = new SaveDataResultToken();
		var searchResponse = new Searching.DirNameSearchResponse();

		yield return HandleSearchRequest( saveDataResultToken, searchResponse, ps5UserId );

		if( saveDataResultToken.IsAnError )
		{
			yield break;
		}

		if( searchResponse.ReturnCode == ReturnCodes.SUCCESS && searchResponse.SaveDataItems != null )
		{
			foreach (var dir in searchResponse.SaveDataItems)
			{
				var deleteResponse = new EmptyResponse();
				var startDeleteResult= StartDeleteDataRequest( deleteResponse, ps5UserId, dir.DirName );

				if( !PS5ErrorCode.Succeeded( startDeleteResult ) )
				{
					yield break;
				}

				while( deleteResponse.Locked )
				{
					yield return null;
				}
			}
		}
	}

	private static int StartShowDialogForErrorRequest( Dialogs.OpenDialogResponse response, int ps5UserId, Dialogs.NewItem newItem, Dialogs.DialogType dialogType, int errorCode )
	{
		try
		{
			var request = new Dialogs.OpenDialogRequest()
			{
				UserId         = ps5UserId,
				DispType       = dialogType,
				Animations     = new Dialogs.AnimationParam( Dialogs.Animation.On, Dialogs.Animation.On ),
				NewItem        = newItem,
				Mode           = Dialogs.DialogMode.ErrorCode,
				ErrorCode      = new Dialogs.ErrorCodeParam(){ ErrorCode = errorCode },	
				IgnoreCallback = true,
			};

			var requestId = Dialogs.OpenDialog( request, response );
			Log( $"StartShowDialogForErrorRequest - started request Id: {requestId}" );
		}
		catch( SaveDataException e )
		{
			Log( $"StartShowDialogForErrorRequest - Exception: {e.ExtendedMessage}" );
			return e.SceErrorCode;
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator ShowDialogForError( int ps5UserId, Dialogs.NewItem newItem, Dialogs.DialogType dialogType, int sceErrorCode, Action< int > onDeleteOperationFinished )
	{
		var showErrorResponse = new Dialogs.OpenDialogResponse();
		var startDeleteResult = StartShowDialogForErrorRequest( showErrorResponse, ps5UserId, newItem, dialogType, sceErrorCode );

		if( !PS5ErrorCode.Succeeded( startDeleteResult ) )
		{
			onDeleteOperationFinished( startDeleteResult );
			yield break;
		}

		while( showErrorResponse.Locked )
		{
			yield return null;
		}

		onDeleteOperationFinished( showErrorResponse.ReturnCodeValue );		
	}


	private static int StartShowNotEnoughSpaceToSaveRequest( Dialogs.OpenDialogResponse response, int ps5UserId, Dialogs.NewItem newItem )
	{
		try
		{
			var request = new Dialogs.OpenDialogRequest()
			{
				UserId         = ps5UserId,
				DispType       = Dialogs.DialogType.Save,
				Animations     = new Dialogs.AnimationParam( Dialogs.Animation.On, Dialogs.Animation.On ),
				NewItem        = newItem,
				Mode           = Dialogs.DialogMode.SystemMsg,
				SystemMessage  = new Dialogs.SystemMessageParam(){ SysMsgType = Dialogs.SystemMessageType.NoSpace },  
				IgnoreCallback = true,
			};

			var requestId = Dialogs.OpenDialog( request, response );
			Log( $"StartShowDialogForErrorRequest - started request Id: {requestId}" );
		}
		catch( SaveDataException e )
		{
			Log( $"StartShowDialogForErrorRequest - Exception: {e.ExtendedMessage}" );
			return e.SceErrorCode;
		}

		return PS5ErrorCode.SCE_OK;
	}
	
	private static IEnumerator HandleNoSpaceToSave( int ps5UserId, Dialogs.NewItem newItem, Action< int > onDeleteOperationFinished )
	{
		var showErrorResponse = new Dialogs.OpenDialogResponse();
		var startDeleteResult = StartShowNotEnoughSpaceToSaveRequest( showErrorResponse, ps5UserId, newItem );

		if( !PS5ErrorCode.Succeeded( startDeleteResult ) )
		{
			onDeleteOperationFinished( startDeleteResult );
			yield break;
		}

		while( showErrorResponse.Locked )
		{
			yield return null;
		}

		onDeleteOperationFinished( showErrorResponse.ReturnCodeValue );		
	}

	private static void Log(object message)
	{
#if PS5_LOG
		Debug.Log(message);
#endif
	}
}
#endif