/*
****************************************************************************************************
* IBScanNFIQ2Api_err.h
*
* DESCRIPTION:
*     Error codes for IBScanNFIQ2.
*     http://www.integratedbiometrics.com
*
* NOTES:
*     Copyright (c) Integrated Biometrics, 2018-2018
*
* HISTORY:
*     2018/01/03  1.0.0  Created.
****************************************************************************************************
*/

#pragma once

/*
****************************************************************************************************
* GENERIC ERROR CODES
****************************************************************************************************
*/
#define IBSU_NFIQ2_STATUS_OK                          0  /* Function completed successfully. */
#define IBSU_ERR_NFIQ2_INVALID_PARAM_VALUE           -1  /* Invalid parameter value. */
#define IBSU_ERR_NFIQ2_MEM_ALLOC                     -2  /* Insufficient memory. */
#define IBSU_ERR_NFIQ2_NOT_SUPPORTED                 -3  /* Requested functionality isn't supported. */
#define IBSU_ERR_NFIQ2_FILE_OPEN                     -4  /* File (USB handle, pipe, or image file) open failed. */
#define IBSU_ERR_NFIQ2_FILE_READ                     -5  /* File (USB handle, pipe, or image file) read failed. */
#define IBSU_ERR_NFIQ2_RESOURCE_LOCKED               -6  /* Failure due to a locked resource. */
#define IBSU_ERR_NFIQ2_MISSING_RESOURCE              -7  /* Failure due to a missing resource (e.g. DLL file). */
#define IBSU_ERR_NFIQ2_INVALID_ACCESS_POINTER        -8  /* Invalid access pointer address. */
#define IBSU_ERR_NFIQ2_THREAD_CREATE                 -9  /* Thread creation failed. */
#define IBSU_ERR_NFIQ2_COMMAND_FAILED                -10  /* Generic command execution failed. */
#define IBSU_ERR_NFIQ2_LIBRARY_UNLOAD_FAILED         -11  /* The library unload failed. */

/*
****************************************************************************************************
* NFIQ2 ERROR CODES
****************************************************************************************************
*/
#define IBSU_ERR_NFIQ2_NOT_INITIALIZED             -900  /* NFIQ2 needs to be initialized. */
#define IBSU_ERR_NFIQ2_FAILED                      -901  /* Getting NFIQ2 score failed */


/*
****************************************************************************************************
* WARNING CODES
****************************************************************************************************
*/
#define IBSU_WRN_NFIQ2_ALREADY_INITIALIZED          901  /* NFIQ2 has already been initialized and is ready to be used. */
#define IBSU_WRN_NFIQ2_API_DEPRECATED               902  /* API function was deprecated. */
