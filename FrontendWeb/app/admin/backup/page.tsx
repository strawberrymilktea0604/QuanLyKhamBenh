'use client'

import { useState, useEffect } from 'react'
import AdminLayout from '@/components/AdminLayout'

interface BackupFile {
  fileName: string
  filePath: string
  fileSizeBytes: number
  fileSizeDisplay: string
  createdDate: string
}

export default function BackupRestorePage() {
  const [backupFiles, setBackupFiles] = useState<BackupFile[]>([])
  const [loading, setLoading] = useState(false)
  const [backupFileName, setBackupFileName] = useState('')
  const [selectedFile, setSelectedFile] = useState<string>('')
  const [showRestoreConfirm, setShowRestoreConfirm] = useState(false)
  const [message, setMessage] = useState<{ type: 'success' | 'error', text: string } | null>(null)

  useEffect(() => {
    loadBackupFiles()
  }, [])

  const loadBackupFiles = async () => {
    try {
      setLoading(true)
      const token = localStorage.getItem('token')
      const response = await fetch('http://localhost:5129/api/BackupRestore/files', {
        headers: {
          'Authorization': `Bearer ${token}`
        }
      })

      if (response.ok) {
        const data = await response.json()
        setBackupFiles(data.data || [])
      } else {
        showMessage('error', 'Không thể tải danh sách file backup')
      }
    } catch (error) {
      showMessage('error', 'Lỗi khi tải danh sách file backup')
    } finally {
      setLoading(false)
    }
  }

  const handleBackup = async () => {
    if (!backupFileName.trim()) {
      showMessage('error', 'Vui lòng nhập tên file backup')
      return
    }

    try {
      setLoading(true)
      const token = localStorage.getItem('token')
      const response = await fetch('http://localhost:5129/api/BackupRestore/backup', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify({ fileName: backupFileName })
      })

      const data = await response.json()

      if (response.ok && data.success) {
        showMessage('success', 'Backup database thành công!')
        setBackupFileName('')
        await loadBackupFiles()
      } else {
        showMessage('error', data.message || 'Backup thất bại')
      }
    } catch (error) {
      showMessage('error', 'Lỗi khi backup database')
    } finally {
      setLoading(false)
    }
  }

  const handleRestoreClick = (fileName: string) => {
    setSelectedFile(fileName)
    setShowRestoreConfirm(true)
  }

  const handleRestoreConfirm = async () => {
    try {
      setLoading(true)
      setShowRestoreConfirm(false)
      const token = localStorage.getItem('token')
      
      const response = await fetch('http://localhost:5129/api/BackupRestore/restore', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify({ fileName: selectedFile })
      })

      const data = await response.json()

      if (response.ok && data.success) {
        showMessage('success', 'Restore database thành công! Hệ thống đã được phục hồi.')
        await loadBackupFiles()
      } else {
        showMessage('error', data.message || 'Restore thất bại')
      }
    } catch (error) {
      showMessage('error', 'Lỗi khi restore database')
    } finally {
      setLoading(false)
      setSelectedFile('')
    }
  }

  const handleDelete = async (fileName: string) => {
    if (!confirm(`Bạn có chắc chắn muốn xóa file backup "${fileName}"?`)) {
      return
    }

    try {
      setLoading(true)
      const token = localStorage.getItem('token')
      const response = await fetch(`http://localhost:5129/api/BackupRestore/files/${encodeURIComponent(fileName)}`, {
        method: 'DELETE',
        headers: {
          'Authorization': `Bearer ${token}`
        }
      })

      const data = await response.json()

      if (response.ok && data.success) {
        showMessage('success', 'Xóa file backup thành công')
        await loadBackupFiles()
      } else {
        showMessage('error', data.message || 'Xóa file thất bại')
      }
    } catch (error) {
      showMessage('error', 'Lỗi khi xóa file backup')
    } finally {
      setLoading(false)
    }
  }

  const showMessage = (type: 'success' | 'error', text: string) => {
    setMessage({ type, text })
    setTimeout(() => setMessage(null), 5000)
  }

  const formatDate = (dateString: string) => {
    const date = new Date(dateString)
    return date.toLocaleString('vi-VN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    })
  }

  return (
    <AdminLayout>
      <div className="space-y-6">
        {/* Header */}
        <div className="bg-white rounded-lg shadow-sm p-6 border">
          <h1 className="text-2xl font-bold text-gray-900">Sao lưu & Phục hồi</h1>
          <p className="text-gray-600 mt-2">Quản lý backup và restore dữ liệu hệ thống</p>
        </div>

        {/* Message Alert */}
        {message && (
          <div className={`p-4 rounded-lg ${message.type === 'success' ? 'bg-green-50 text-green-800 border border-green-200' : 'bg-red-50 text-red-800 border border-red-200'}`}>
            <p className="font-medium">{message.text}</p>
          </div>
        )}

        {/* Backup Section */}
        <div className="bg-white rounded-lg shadow-sm p-6 border">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Tạo bản sao lưu mới</h2>
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">
                Tên file backup
              </label>
              <input
                type="text"
                value={backupFileName}
                onChange={(e) => setBackupFileName(e.target.value)}
                placeholder="Ví dụ: QuanLyKhamBenh_20251202"
                className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                disabled={loading}
              />
              <p className="text-sm text-gray-500 mt-1">
                Để trống để tự động tạo tên với timestamp. Phần mở rộng .bak sẽ được tự động thêm vào.
              </p>
            </div>
            <button
              onClick={handleBackup}
              disabled={loading}
              className="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition-colors"
            >
              {loading ? 'Đang xử lý...' : 'Tạo Backup'}
            </button>
          </div>
        </div>

        {/* Backup Files List */}
        <div className="bg-white rounded-lg shadow-sm border">
          <div className="p-6 border-b">
            <div className="flex items-center justify-between">
              <h2 className="text-lg font-semibold text-gray-900">Danh sách file backup</h2>
              <button
                onClick={loadBackupFiles}
                disabled={loading}
                className="px-4 py-2 text-blue-600 hover:bg-blue-50 rounded-lg transition-colors disabled:opacity-50"
              >
                Làm mới
              </button>
            </div>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-gray-50 border-b">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Tên file
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Kích thước
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Ngày tạo
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Thao tác
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {backupFiles.length === 0 ? (
                  <tr>
                    <td colSpan={4} className="px-6 py-8 text-center text-gray-500">
                      {loading ? 'Đang tải...' : 'Chưa có file backup nào'}
                    </td>
                  </tr>
                ) : (
                  backupFiles.map((file) => (
                    <tr key={file.fileName} className="hover:bg-gray-50">
                      <td className="px-6 py-4 text-sm text-gray-900 font-medium">
                        {file.fileName}
                      </td>
                      <td className="px-6 py-4 text-sm text-gray-600">
                        {file.fileSizeDisplay}
                      </td>
                      <td className="px-6 py-4 text-sm text-gray-600">
                        {formatDate(file.createdDate)}
                      </td>
                      <td className="px-6 py-4 text-sm text-right space-x-2">
                        <button
                          onClick={() => handleRestoreClick(file.fileName)}
                          disabled={loading}
                          className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition-colors"
                        >
                          Restore
                        </button>
                        <button
                          onClick={() => handleDelete(file.fileName)}
                          disabled={loading}
                          className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition-colors"
                        >
                          Xóa
                        </button>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>

        {/* Warning Note */}
        <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
          <h3 className="text-sm font-semibold text-yellow-800 mb-2">⚠️ Lưu ý quan trọng:</h3>
          <ul className="text-sm text-yellow-700 space-y-1 list-disc list-inside">
            <li>Chức năng Backup sẽ tạo file sao lưu toàn bộ cơ sở dữ liệu</li>
            <li>Chức năng Restore sẽ <strong>KHÔI PHỤC LẠI</strong> toàn bộ dữ liệu về thời điểm backup</li>
            <li>Khi Restore, <strong>TẤT CẢ NGƯỜI DÙNG SẼ BỊ NGẮT KẾT NỐI</strong> tạm thời (khoảng 10-30 giây)</li>
            <li>Mọi dữ liệu sau thời điểm backup sẽ bị mất khi thực hiện Restore</li>
            <li>Chỉ thực hiện Restore khi thực sự cần thiết và đã thông báo cho người dùng</li>
          </ul>
        </div>

        {/* Restore Confirmation Modal */}
        {showRestoreConfirm && (
          <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6">
              <h3 className="text-xl font-bold text-red-600 mb-4">⚠️ Xác nhận Restore</h3>
              <div className="space-y-3 mb-6">
                <p className="text-gray-700">
                  Bạn có chắc chắn muốn restore database từ file:
                </p>
                <p className="font-semibold text-gray-900 bg-gray-100 p-2 rounded">
                  {selectedFile}
                </p>
                <div className="bg-red-50 border border-red-200 rounded p-3">
                  <p className="text-sm text-red-800 font-medium mb-2">Hành động này sẽ:</p>
                  <ul className="text-sm text-red-700 space-y-1 list-disc list-inside">
                    <li>Ngắt kết nối toàn bộ người dùng</li>
                    <li>Đưa dữ liệu về thời điểm backup</li>
                    <li>Xóa mọi dữ liệu sau thời điểm backup</li>
                    <li>Không thể hoàn tác</li>
                  </ul>
                </div>
              </div>
              <div className="flex gap-3">
                <button
                  onClick={() => {
                    setShowRestoreConfirm(false)
                    setSelectedFile('')
                  }}
                  className="flex-1 px-4 py-2 bg-gray-200 text-gray-800 rounded-lg hover:bg-gray-300 transition-colors"
                >
                  Hủy bỏ
                </button>
                <button
                  onClick={handleRestoreConfirm}
                  className="flex-1 px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 transition-colors font-semibold"
                >
                  Xác nhận Restore
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </AdminLayout>
  )
}
